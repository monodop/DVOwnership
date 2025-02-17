﻿using DV.Logic.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVOwnership
{
    public static class ProceduralJobGenerators
    {
        public static JobChainController GenerateHaulChainJobForCars(System.Random rng, List<Car> carsForJob, CargoGroup cargoGroup, StationController originController)
        {
            return GenerateHaulChainJobForCars(rng, carsForJob, originController, Utilities.GetRandomFrom(rng, cargoGroup.stations));
        }

        private static JobChainController GenerateHaulChainJobForCars(System.Random rng, List<Car> carsForJob, StationController originController, StationController destinationController)
        {
            var yto = YardTracksOrganizer.Instance;

            List<CargoType> cargoTypes = (from car in carsForJob select car.CurrentCargoTypeInCar).ToList();
            List<float> cargoAmounts = (from car in carsForJob select car.LoadedCargoAmount).ToList();

            var tracksForCars = (from car in carsForJob select car.CurrentTrack).ToHashSet();
            if (tracksForCars.Count != 1)
            {
                DVOwnership.LogError($"Expected only one starting track for {JobType.Transport} job, but got {tracksForCars.Count}.");
                return null;
            }
            var startingTrack = tracksForCars.First();

            float approxLengthOfWholeTrain = yto.GetTotalCarsLength(carsForJob) + yto.GetSeparationLengthBetweenCars(carsForJob.Count);

            JobLicenses jobLicenses = LicenseManager.GetRequiredLicensesForCargoTypes(cargoTypes) | LicenseManager.GetRequiredLicenseForNumberOfTransportedCars(carsForJob.Count);

            var possibleDestinationTracks = yto.FilterOutTracksWithoutRequiredFreeSpace(destinationController.logicStation.yard.TransferInTracks, approxLengthOfWholeTrain);
            if (possibleDestinationTracks.Count < 1)
            {
                DVOwnership.LogWarning($"Station[{originController.logicStation.ID}] couldn't find an inbound track with enough free space for the job. ({approxLengthOfWholeTrain})");
                return null;
            }
            var destinationTrack = Utilities.GetRandomFrom(rng, possibleDestinationTracks);

            var gameObject = new GameObject($"ChainJob[{JobType.Transport}]: {originController.logicStation.ID} - {destinationController.logicStation.ID}");
            gameObject.transform.SetParent(originController.transform);
            // This class is patched to do next-in-chain job generation
            var jobChainController = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            jobChainController.trainCarsForJobChain = Utilities.ConvertLogicCarsToTrainCars(carsForJob).ToList();

            var stationsChainData = new StationsChainData(originController.stationInfo.YardID, destinationController.stationInfo.YardID);

            float distanceBetweenStations = JobPaymentCalculator.GetDistanceBetweenStations(originController, destinationController);
            float bonusTimeLimit = JobPaymentCalculator.CalculateHaulBonusTimeLimit(distanceBetweenStations);
            float baseWage = JobPaymentCalculator.CalculateJobPayment(JobType.Transport, distanceBetweenStations, Utilities.ExtractPaymentCalculationData(carsForJob));
            JobLicenses requiredLicenses = jobLicenses | LicenseManager.GetRequiredLicensesForJobType(JobType.Transport);

            var jobDefinition = PopulateHaulJobDefinitionWithExistingCars(jobChainController.jobChainGO, originController.logicStation, startingTrack, destinationTrack, carsForJob, cargoTypes, cargoAmounts, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);

            jobChainController.AddJobDefinitionToChain(jobDefinition);
            jobChainController.FinalizeSetupAndGenerateFirstJob();

            return jobChainController;
        }

        public static JobChainController GenerateUnloadChainJobForCars(System.Random rng, List<Car> carsForJob, CargoGroup cargoGroup, StationController destinationController)
        {
            return GenerateUnloadChainJobForCars(rng, carsForJob, Utilities.GetRandomFrom(rng, cargoGroup.stations), destinationController);
        }

        private static JobChainController GenerateUnloadChainJobForCars(System.Random rng, List<Car> carsForJob, StationController originController, StationController destinationController)
        {
            var yto = YardTracksOrganizer.Instance;
            var generationRuleset = destinationController.proceduralJobsRuleset;

            List<CargoType> cargoTypes = (from car in carsForJob select car.CurrentCargoTypeInCar).ToList();

            var tracksForCars = (from car in carsForJob select car.CurrentTrack).ToHashSet();
            if (tracksForCars.Count != 1)
            {
                DVOwnership.LogError($"Expected only one starting track for {JobType.ShuntingUnload} job, but got {tracksForCars.Count}.");
                return null;
            }
            var startingTrack = tracksForCars.First();

            float approxLengthOfWholeTrain = yto.GetTotalCarsLength(carsForJob) + yto.GetSeparationLengthBetweenCars(carsForJob.Count);

            JobLicenses jobLicenses = LicenseManager.GetRequiredLicensesForCargoTypes(cargoTypes) | LicenseManager.GetRequiredLicenseForNumberOfTransportedCars(carsForJob.Count);

            var warehouseMachinesThatSupportCargoTypes = destinationController.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(cargoTypes);
            if (warehouseMachinesThatSupportCargoTypes.Count == 0)
            {
                DVOwnership.LogError($"Station[{destinationController.logicStation.ID}] doesn't have a warehouse machine that supports all cargo types for the job. [{cargoTypes.Aggregate("", (str, type) => $"{str}, {type}")}]");
                return null;
            }
            warehouseMachinesThatSupportCargoTypes.RemoveAll((machine) => machine.WarehouseTrack.length < (double)approxLengthOfWholeTrain);
            if (warehouseMachinesThatSupportCargoTypes.Count == 0)
            {
                DVOwnership.LogDebug(() => $"Station[{destinationController.logicStation.ID}] doesn't have a warehouse track long enough for the job. ({approxLengthOfWholeTrain})");
                return null;
            }
            var warehouseMachine = Utilities.GetRandomFrom(rng, warehouseMachinesThatSupportCargoTypes);

            var randomSortingOfCarsOnTracks = Utilities.GetRandomSortingOfCarsOnTracks(rng, destinationController.logicStation.yard.StorageTracks, carsForJob, generationRuleset.maxShuntingStorageTracks);
            if (randomSortingOfCarsOnTracks == null)
            {
                DVOwnership.LogDebug(() => $"Station[{destinationController.logicStation.ID}] couldn't assign cars to storage tracks.");
                return null;
            }

            var gameObject = new GameObject($"ChainJob[{JobType.ShuntingUnload}]: {originController.logicStation.ID} - {destinationController.logicStation.ID}");
            gameObject.transform.SetParent(destinationController.transform);
            // This class is patched to do next-in-chain job generation
            var jobChainController = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            jobChainController.trainCarsForJobChain = Utilities.ConvertLogicCarsToTrainCars(carsForJob).ToList();

            var stationsChainData = new StationsChainData(originController.stationInfo.YardID, destinationController.stationInfo.YardID);

            int countTracks = randomSortingOfCarsOnTracks.Count;
            float bonusTimeLimit = JobPaymentCalculator.CalculateShuntingBonusTimeLimit(countTracks);
            float distanceInMeters = 500f * countTracks;
            float baseWage = JobPaymentCalculator.CalculateJobPayment(JobType.ShuntingUnload, distanceInMeters, Utilities.ExtractPaymentCalculationData(carsForJob));
            JobLicenses requiredLicenses = jobLicenses | LicenseManager.GetRequiredLicensesForJobType(JobType.ShuntingUnload);

            var carsPerCargoType = Utilities.ExtractCarsPerCargoType(carsForJob);

            var jobDefinition = PopulateShuntingUnloadJobDefinitionWithExistingCars(jobChainController.jobChainGO, destinationController.logicStation, startingTrack, warehouseMachine, carsPerCargoType, randomSortingOfCarsOnTracks, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);

            jobChainController.AddJobDefinitionToChain(jobDefinition);
            jobChainController.FinalizeSetupAndGenerateFirstJob();

            return jobChainController;
        }

        public static JobChainController GenerateLoadChainJobForCars(System.Random rng, List<List<Car>> carSetsForJob, CargoGroup cargoGroup, StationController originController)
        {
            var yto = YardTracksOrganizer.Instance;

            List<CarsPerTrack> carsPerStartingTrack = new List<CarsPerTrack>();
            List<Car> carsForJob = new List<Car>();
            List<CargoType> cargoTypes = new List<CargoType>();
            foreach (var carSet in carSetsForJob)
            {
                var track = carSet.Aggregate(null as Track, (t, c) =>
                {
                    if (t == null && c.FrontBogieTrack == c.RearBogieTrack) { return c.FrontBogieTrack; }
                    return t;
                });
                if (track == null)
                {
                    track = carSet[0].FrontBogieTrack;
                    DVOwnership.LogWarning($"Station[{originController.logicStation.ID}] couldn't determine track of cars for shunting load job. Using track {track} in order to continue.");
                }
                var carsPerTrack = new CarsPerTrack(track, carSet);
                carsPerStartingTrack.Add(carsPerTrack);

                foreach (var car in carSet)
                {
                    carsForJob.Add(car);
                    var potentialCargoTypes = from cargoType in cargoGroup.cargoTypes
                                              where CargoTypes.CanCarContainCargoType(car.carType, cargoType)
                                              select cargoType;
                    if (potentialCargoTypes.Count() < 1)
                    {
                        DVOwnership.LogError($"Station[{originController.logicStation.ID}] found no matching cargo types for car type {car.carType} and cargo group cargo types [{cargoGroup.cargoTypes.Aggregate("", (str, type) => $"{str}, {type}")}].");
                        return null;
                    }
                    var selectedCargoType = Utilities.GetRandomFrom(rng, potentialCargoTypes);
                    cargoTypes.Add(selectedCargoType);
                }
            }
            List<CarsPerCargoType> carsPerCargoTypes = Utilities.ExtractCarsPerCargoType(carsForJob, cargoTypes);

            float approxLengthOfWholeTrain = yto.GetTotalCarsLength(carsForJob) + yto.GetSeparationLengthBetweenCars(carsForJob.Count);

            JobLicenses jobLicenses = LicenseManager.GetRequiredLicensesForCargoTypes(cargoTypes) | LicenseManager.GetRequiredLicenseForNumberOfTransportedCars(carsForJob.Count);

            var warehouseMachinesThatSupportCargoTypes = originController.logicStation.yard.GetWarehouseMachinesThatSupportCargoTypes(cargoTypes);
            if (warehouseMachinesThatSupportCargoTypes.Count == 0)
            {
                DVOwnership.LogError($"Station[{originController.logicStation.ID}] doesn't have a warehouse machine that supports all cargo types for the job. [{cargoTypes.Aggregate("", (str, type) => $"{str}, {type}")}]");
                return null;
            }
            warehouseMachinesThatSupportCargoTypes.RemoveAll((machine) => machine.WarehouseTrack.length < (double)approxLengthOfWholeTrain);
            if (warehouseMachinesThatSupportCargoTypes.Count == 0)
            {
                DVOwnership.LogDebug(() => $"Station[{originController.logicStation.ID}] doesn't have a warehouse track long enough for the job. ({approxLengthOfWholeTrain})");
                return null;
            }
            var warehouseMachine = Utilities.GetRandomFrom(rng, warehouseMachinesThatSupportCargoTypes);

            var destinationController = Utilities.GetRandomFrom(rng, cargoGroup.stations);
            var possibleDestinationTracks = yto.FilterOutTracksWithoutRequiredFreeSpace(originController.logicStation.yard.TransferOutTracks, approxLengthOfWholeTrain);
            if (possibleDestinationTracks.Count < 1)
            {
                DVOwnership.LogWarning($"Station[{originController.logicStation.ID}] couldn't find an outbound track with enough free space for the job. ({approxLengthOfWholeTrain})");
                return null;
            }
            var destinationTrack = Utilities.GetRandomFrom(rng, possibleDestinationTracks);

            var gameObject = new GameObject($"ChainJob[{JobType.ShuntingLoad}]: {originController.logicStation.ID} - {destinationController.logicStation.ID}");
            gameObject.transform.SetParent(originController.transform);
            // This class is patched to do next-in-chain job generation
            var jobChainController = new JobChainControllerWithEmptyHaulGeneration(gameObject);
            jobChainController.trainCarsForJobChain = Utilities.ConvertLogicCarsToTrainCars(carsForJob).ToList();

            var stationsChainData = new StationsChainData(originController.stationInfo.YardID, destinationController.stationInfo.YardID);

            int countTracks = carsPerStartingTrack.Count;
            float bonusTimeLimit = JobPaymentCalculator.CalculateShuntingBonusTimeLimit(countTracks);
            float distanceInMeters = 500f * countTracks;
            float baseWage = JobPaymentCalculator.CalculateJobPayment(JobType.ShuntingLoad, distanceInMeters, Utilities.ExtractPaymentCalculationData(carsForJob));
            JobLicenses requiredLicenses = jobLicenses | LicenseManager.GetRequiredLicensesForJobType(JobType.ShuntingLoad);

            var jobDefinition = PopulateShuntingLoadJobDefinitionWithExistingCars(jobChainController.jobChainGO, originController.logicStation, carsPerStartingTrack, warehouseMachine, carsPerCargoTypes, destinationTrack, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);

            jobChainController.AddJobDefinitionToChain(jobDefinition);
            jobChainController.FinalizeSetupAndGenerateFirstJob();

            return jobChainController;
        }

        private static StaticTransportJobDefinition PopulateHaulJobDefinitionWithExistingCars(GameObject chainJobGO, Station logicStation, Track startingTrack, Track destinationTrack, List<Car> logicCarsToHaul, List<CargoType> cargoTypePerCar, List<float> cargoAmountPerCar, float bonusTimeLimit, float baseWage, StationsChainData stationsChainData, JobLicenses requiredLicenses)
        {
            var jobDefinition = chainJobGO.AddComponent<StaticTransportJobDefinition>();
            jobDefinition.PopulateBaseJobDefinition(logicStation, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);
            jobDefinition.startingTrack = startingTrack;
            jobDefinition.trainCarsToTransport = logicCarsToHaul;
            jobDefinition.transportedCargoPerCar = cargoTypePerCar;
            jobDefinition.cargoAmountPerCar = cargoAmountPerCar;
            jobDefinition.forceCorrectCargoStateOnCars = true;
            jobDefinition.destinationTrack = destinationTrack;
            return jobDefinition;
        }

        private static StaticShuntingUnloadJobDefinition PopulateShuntingUnloadJobDefinitionWithExistingCars(GameObject chainJobGO, Station logicStation, Track startingTrack, WarehouseMachine unloadMachine, List<CarsPerCargoType> carsPerCargoType, List<CarsPerTrack> carsPerDestinationTrack, float bonusTimeLimit, float baseWage, StationsChainData stationsChainData, JobLicenses requiredLicenses)
        {
            var jobDefinition = chainJobGO.AddComponent<StaticShuntingUnloadJobDefinition>();
            jobDefinition.PopulateBaseJobDefinition(logicStation, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);
            jobDefinition.startingTrack = startingTrack;
            jobDefinition.unloadMachine = unloadMachine;
            jobDefinition.unloadData = carsPerCargoType;
            jobDefinition.carsPerDestinationTrack = carsPerDestinationTrack;
            jobDefinition.forceCorrectCargoStateOnCars = true;
            return jobDefinition;
        }

        private static StaticShuntingLoadJobDefinition PopulateShuntingLoadJobDefinitionWithExistingCars(GameObject chainJobGO, Station logicStation, List<CarsPerTrack> carsPerStartingTrack, WarehouseMachine loadMachine, List<CarsPerCargoType> carsPerCargoType, Track destinationTrack, float bonusTimeLimit, float baseWage, StationsChainData stationsChainData, JobLicenses requiredLicenses)
        {
            var jobDefinition = chainJobGO.AddComponent<StaticShuntingLoadJobDefinition>();
            jobDefinition.PopulateBaseJobDefinition(logicStation, bonusTimeLimit, baseWage, stationsChainData, requiredLicenses);
            jobDefinition.carsPerStartingTrack = carsPerStartingTrack;
            jobDefinition.loadMachine = loadMachine;
            jobDefinition.loadData = carsPerCargoType;
            jobDefinition.destinationTrack = destinationTrack;
            jobDefinition.forceCorrectCargoStateOnCars = true;
            return jobDefinition;
        }

        public static void SetDestination(JobChainController controller, string destinationID)
        {
            var rollingStock = SingletonBehaviour<RollingStockManager>.Instance;
            var trainCars = controller.trainCarsForJobChain;
            var equipments = from trainCar in trainCars select rollingStock.FindByTrainCar(trainCar);
            foreach (var equipment in equipments)
            {
                equipment.SetDestination(destinationID);
            }
        }

        public static void GenerateContinuationTransportJob(JobChainController jobChainController, StationController originController, StationController destinationController)
        {
            var trainCars = jobChainController?.trainCarsForJobChain;
            if (trainCars == null)
            {
                DVOwnership.LogError($"Expected trainCarsForJobChain to exist, but it does not. Can't generate transport job.");
                return;
            }

            int tickCount = Environment.TickCount;
            System.Random rng = new System.Random(tickCount);
            var carsForJob = from trainCar in trainCars select trainCar.logicCar;
            var completedJobID = jobChainController?.currentJobInChain?.ID;
            var originYardID = originController.stationInfo.YardID;
            var destinationYardID = destinationController.stationInfo.YardID;

            jobChainController = GenerateHaulChainJobForCars(rng, carsForJob.ToList(), originController, destinationController);

            if (jobChainController == null)
            {
                DVOwnership.LogError($"Couldn't generate a freight haul job to continue {completedJobID}!\n\torigin: {originYardID}\n\tdestination: {destinationYardID}\n\ttrain cars: {string.Join(", ", from trainCar in trainCars select trainCar.ID)}");
                return;
            }

            DVOwnership.Log($"Generated freight haul job {jobChainController.currentJobInChain.ID} as continuation of {completedJobID}.");
        }

        public static void GenerateContinuationUnloadJob(JobChainController jobChainController, StationController originController, StationController destinationController)
        {
            var trainCars = jobChainController?.trainCarsForJobChain;
            if (trainCars == null)
            {
                DVOwnership.LogError($"Expected trainCarsForJobChain to exist, but it does not. Can't generate unload job.");
                return;
            }

            int tickCount = Environment.TickCount;
            System.Random rng = new System.Random(tickCount);
            var carsForJob = from trainCar in trainCars select trainCar.logicCar;
            var completedJobID = jobChainController?.currentJobInChain?.ID;
            var originYardID = originController.stationInfo.YardID;
            var destinationYardID = destinationController.stationInfo.YardID;

            jobChainController = GenerateUnloadChainJobForCars(rng, carsForJob.ToList(), originController, destinationController);

            if (jobChainController == null)
            {
                DVOwnership.LogError($"Couldn't generate an unload job to continue {completedJobID}!\n\torigin: {originYardID}\n\tdestination: {destinationYardID}\n\ttrain cars: {string.Join(", ", from trainCar in trainCars select trainCar.ID)}");
                return;
            }

            DVOwnership.Log($"Generated unload job {jobChainController.currentJobInChain.ID} as continuation of {completedJobID}.");
        }
    }
}
