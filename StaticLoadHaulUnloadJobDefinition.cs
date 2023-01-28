using DV.Logic.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DVOwnership
{
    public class StaticLoadHaulUnloadJobDefinition : StaticJobDefinition
    {
        [Header("LoadHaulUnload job parameters")]
        [Tooltip("Set of cars per their starting track")]
        public List<CarsPerTrack> carsPerStartingTrack;

        [Tooltip("WarehouseMachine where cars should be loaded with cargo")]
        public WarehouseMachine loadMachine;

        [Tooltip("WarehouseMachine where cars should be unloaded with cargo")]
        public WarehouseMachine unloadMachine;

        [Tooltip("Set of cars per cargo type they should load")]
        public List<CarsPerCargoType> loadData;

        [Tooltip("Set to true, if you want to force correct state of the cars, otherwise if state of cars regarding cargo is not correct, it will generate errors")]
        public bool forceCorrectCargoStateOnCars;

        public override JobDefinitionDataBase GetJobDefinitionSaveData()
        {
            CarGuidsPerTrackId[] array = new CarGuidsPerTrackId[carsPerStartingTrack.Count];
            for (int i = 0; i < carsPerStartingTrack.Count; i++)
            {
                CarsPerTrack carsPerTrack = carsPerStartingTrack[i];
                string[] guidsFromCars = StaticJobDefinition.GetGuidsFromCars(carsPerTrack.cars);
                if (guidsFromCars == null)
                {
                    throw new Exception("Couldn't extract carGuidsPerStartingTrack");
                }

                string fullID = carsPerTrack.track.ID.FullID;
                array[i] = new CarGuidsPerTrackId(fullID, guidsFromCars);
            }

            CarGuidsPerCargo[] array2 = new CarGuidsPerCargo[loadData.Count];
            for (int j = 0; j < loadData.Count; j++)
            {
                CarsPerCargoType carsPerCargoType = loadData[j];
                string[] guidsFromCars2 = StaticJobDefinition.GetGuidsFromCars(carsPerCargoType.cars);
                if (guidsFromCars2 == null)
                {
                    throw new Exception("Couldn't extract carGuidsPerCargo");
                }

                array2[j] = new CarGuidsPerCargo(carsPerCargoType.cargoType, guidsFromCars2, carsPerCargoType.totalCargoAmount);
            }

            return new LoadHaulUnloadJobDefinitionData(
                timeLimitForJob,
                initialWage,
                logicStation.ID,
                chainData.chainOriginYardId,
                chainData.chainDestinationYardId,
                (int)requiredLicenses,
                array,
                array2,
                loadMachine.ID,
                unloadMachine.ID
            );
        }

        public override List<TrackReservation> GetRequiredTrackReservations()
        {
            return new List<TrackReservation>();
        }

        protected override void GenerateJob(Station jobOriginStation, float jobTimeLimit = 0f, float initialWage = 0f, string forcedJobId = null, JobLicenses requiredLicenses = JobLicenses.Basic)
        {
            if (carsPerStartingTrack != null && carsPerStartingTrack.Count > 0 && loadMachine != null && loadData != null && loadData.Count > 0)
            {
                base.job = CreateLoadHaulUnloadJob(
                    jobOriginStation,
                    chainData,
                    carsPerStartingTrack,
                    loadMachine,
                    unloadMachine,
                    loadData,
                    forceCorrectCargoStateOnCars,
                    jobTimeLimit,
                    initialWage,
                    forcedJobId,
                    requiredLicenses
                );
                return;
            }

            carsPerStartingTrack = null;
            loadMachine = null;
            unloadMachine = null;
            loadData = null;
            base.job = null;
            Debug.LogError("ShuntingLoad job not created, bad parameters", this);
        }

        private static List<CargoType> GetCargoTypePerCar(List<CarsPerCargoType> carsPerCargoTypeData)
        {
            List<CargoType> list = new List<CargoType>();
            for (int i = 0; i < carsPerCargoTypeData.Count; i++)
            {
                List<Car> cars = carsPerCargoTypeData[i].cars;
                CargoType cargoType = carsPerCargoTypeData[i].cargoType;
                for (int j = 0; j < cars.Count; j++)
                {
                    list.Add(cargoType);
                }
            }

            return list;
        }

        public static Job CreateLoadHaulUnloadJob(
            Station jobOriginStation,
            StationsChainData chainData,
            List<CarsPerTrack> startingTracksData,
            WarehouseMachine loadMachine,
            WarehouseMachine unloadMachine,
            List<CarsPerCargoType> carsData,
            bool forceDumpCargoIfCarsNotEmpty = false,
            float timeLimit = 0f,
            float initialWage = 0f,
            string forcedJobId = null,
            JobLicenses requiredLicenses = JobLicenses.Basic)
        {
            if (startingTracksData == null || startingTracksData.Count == 0)
            {
                throw new Exception(string.Format("Error while creating {0} job, {1} is null or empty!", JobType.ComplexTransport, "startingTracksData"));
            }

            if (carsData == null || carsData.Count == 0)
            {
                throw new Exception(string.Format("Error while creating {0} job, {1} is null or empty!", JobType.ComplexTransport, "carsLoadData"));
            }


            List<Task> list = new List<Task>();
            for (int j = 0; j < startingTracksData.Count; j++)
            {
                TransportTask item = JobsGenerator.CreateTransportTask(startingTracksData[j].cars, loadMachine.WarehouseTrack, startingTracksData[j].track);
                list.Add(item);
            }

            ParallelTasks stageTask = new ParallelTasks(list, 0L);
            int i;
            for (i = 0; i < carsData.Count; i++)
            {
                if (carsData[i].cars.Any((Car car) => !CargoTypes.CanCarContainCargoType(car.carType, carsData[i].cargoType)))
                {
                    throw new Exception(string.Format("Error while creating {0} job, not all cars from {1}[{2}] can carry {3}!", JobType.ShuntingLoad, "carsLoadData", i, carsData[i].cargoType));
                }

                if (carsData[i].cars.Select((Car car) => car.capacity).Sum() < carsData[i].totalCargoAmount)
                {
                    throw new Exception(string.Format("Error while creating {0} job, {1} {2} to load is beyond {3}[{4}].cars capacity!", JobType.ShuntingLoad, carsData[i].totalCargoAmount, carsData[i].cargoType, "carsLoadData", i));
                }

                if (!loadMachine.IsCargoSupported(carsData[i].cargoType))
                {
                    throw new Exception(string.Format("Error while creating {0} job, cargo type we want to load [{1}] is not supported by {2}", JobType.ShuntingLoad, carsData[i].cargoType, "loadMachine"));
                }

                if (!(carsData[i].cars.Select((Car car) => car.LoadedCargoAmount).Sum() > 0f) && !carsData[i].cars.Any((Car car) => car.CurrentCargoTypeInCar != CargoType.None))
                {
                    continue;
                }

                if (forceDumpCargoIfCarsNotEmpty)
                {
                    carsData[i].cars.ForEach(delegate (Car car)
                    {
                        car.DumpCargo();
                    });
                }
                else
                {
                    Debug.LogWarning("Initial cargo state on car is not correct. This is valid only when loading save game!");
                }
            }

            List<Task> list2 = new List<Task>();
            for (int k = 0; k < carsData.Count; k++)
            {
                list2.Add(new WarehouseTask(carsData[k].cars, WarehouseTaskType.Loading, loadMachine, carsData[k].cargoType, carsData[k].totalCargoAmount, 0L));
            }

            ParallelTasks loadTask = new ParallelTasks(list2, 0L);
            List<CargoType> cargoTypePerCar = GetCargoTypePerCar(carsData);
            TransportTask haulTask = JobsGenerator.CreateTransportTask(carsData.SelectMany((CarsPerCargoType loadData) => loadData.cars).ToList(), unloadMachine.WarehouseTrack, loadMachine.WarehouseTrack, cargoTypePerCar);

            List<Task> list3 = new List<Task>();
            for (int l = 0; l < carsData.Count; l++)
            {
                list3.Add(new WarehouseTask(carsData[l].cars, WarehouseTaskType.Unloading, unloadMachine, carsData[l].cargoType, carsData[l].totalCargoAmount, 0L));
            }
            ParallelTasks unloadTask = new ParallelTasks(list3, 0L);

            Job job = new Job(new SequentialTasks(new List<Task>
            {
                stageTask,
                loadTask,
                haulTask,
                unloadTask,
            }, 0L), JobType.ComplexTransport, timeLimit, initialWage, chainData, forcedJobId, requiredLicenses);
            jobOriginStation.AddJobToStation(job);
            return job;
        }
    }

    public class LoadHaulUnloadJobDefinitionData : JobDefinitionDataBase
    {
        public CarGuidsPerTrackId[] carGuidsPerStartingTrackId;

        public CarGuidsPerCargo[] carGuidsPerLoadCargo;

        public string loadMachineId;

        public string unloadMachineId;

        public LoadHaulUnloadJobDefinitionData(
            float timeLimitForJob,
            float initialWage,
            string stationId,
            string originStationId,
            string destinationStationId,
            int requiredLicenses,
            CarGuidsPerTrackId[] carGuidsPerStartingTrackId,
            CarGuidsPerCargo[] carGuidsPerLoadCargo,
            string loadMachineId,
            string unloadMachineId)
            : base(timeLimitForJob, initialWage, stationId, originStationId, destinationStationId, requiredLicenses)
        {
            this.carGuidsPerStartingTrackId = carGuidsPerStartingTrackId;
            this.carGuidsPerLoadCargo = carGuidsPerLoadCargo;
            this.loadMachineId = loadMachineId;
            this.unloadMachineId = unloadMachineId;
        }
    }
}
