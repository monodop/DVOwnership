﻿using DV.Logic.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DVOwnership
{
    public static class Utilities
    {
        public static T GetRandomFrom<T>(System.Random rng, IEnumerable<T> enumerable)
        {
            var index = rng.Next(enumerable.Count());
            return enumerable.ElementAt(index);
        }

        public static List<CarsPerTrack> GetRandomSortingOfCarsOnTracks(System.Random rng, List<Track> tracks, List<Car> allCarsForJobChain, int maxNumberOfStorageTracks)
        {
            if (tracks == null || tracks.Count == 0) { return null; }

            int numCars = allCarsForJobChain.Count;
            int numTracks = Mathf.Min(new int[]
            {
                rng.Next(1, maxNumberOfStorageTracks + 1),
                tracks.Count,
                numCars
            });
            int averageNumCarsPerTrack = Mathf.FloorToInt((float)numCars / (float)numTracks);

            List<int> numCarsPerTracks = new List<int>();
            int numCarsAccountedFor = 0;
            for (int i = 0; i < numTracks; ++i)
            {
                int numCarsForCurrentTrack;
                if (i == numTracks - 1)
                {
                    numCarsForCurrentTrack = numCars - numCarsAccountedFor;
                }
                else
                {
                    numCarsForCurrentTrack = rng.Next(1, averageNumCarsPerTrack + 1);
                }
                if (numCarsForCurrentTrack < 1)
                {
                    DVOwnership.LogError("Assigned zero cars to a track. This should never happen!");
                }
                numCarsPerTracks.Add(numCarsForCurrentTrack);
                numCarsAccountedFor += numCarsForCurrentTrack;
            }
            numCarsPerTracks.Sort((a, b) => b - a); // reverse sort

            YardTracksOrganizer yto = SingletonBehaviour<YardTracksOrganizer>.Instance;
            List<CarsPerTrack> carsPerTracks = new List<CarsPerTrack>();
            tracks = new List<Track>(tracks); // cloning is required to prevent modifying the external list

            for (int index = 0, cursor = 0; index < numCarsPerTracks.Count; cursor += numCarsPerTracks[index++])
            {
                int numCarsForCurrentTrack = numCarsPerTracks[index];
                List<Car> carsForCurrentTrack = allCarsForJobChain.GetRange(cursor, numCarsForCurrentTrack);

                float approximateLengthOfCarsForCurrentTrack = yto.GetTotalCarsLength(carsForCurrentTrack) + yto.GetSeparationLengthBetweenCars(carsForCurrentTrack.Count);

                List<Track> tracksWithRequiredFreeSpace = yto.FilterOutTracksWithoutRequiredFreeSpace(tracks, approximateLengthOfCarsForCurrentTrack);
                if (tracksWithRequiredFreeSpace.Count < 1)
                {
                    DVOwnership.LogWarning($"Couldn't find a track with enough free space. ({approximateLengthOfCarsForCurrentTrack})");
                    return null;
                }

                Track track = Utilities.GetRandomFrom(rng, tracksWithRequiredFreeSpace);
                tracks.Remove(track);
                carsPerTracks.Add(new CarsPerTrack(track, carsForCurrentTrack));
            }

            return carsPerTracks;
        }

        public static PaymentCalculationData ExtractPaymentCalculationData(List<Car> cars)
        {
            return ExtractPaymentCalculationData(cars, cars.Select(car => car.CurrentCargoTypeInCar).ToList());
        }
        public static PaymentCalculationData ExtractPaymentCalculationData(List<Car> cars, List<CargoType> cargoTypes)
        {
            return ExtractPaymentCalculationData(cars.Select(car => car.carType).ToList(), cargoTypes);
        }
        public static PaymentCalculationData ExtractPaymentCalculationData(List<TrainCarType> carTypes, List<CargoType> cargoTypes)
        {
            Dictionary<TrainCarType, int> countEachCarType = new Dictionary<TrainCarType, int>();
            Dictionary<CargoType, int> countEachCargoType = new Dictionary<CargoType, int>();

            foreach (var carType in carTypes)
            {
                if (!countEachCarType.ContainsKey(carType)) { countEachCarType.Add(carType, 0); }
                countEachCarType[carType]++;
            }

            foreach (var cargoType in cargoTypes)
            {
                if (!countEachCargoType.ContainsKey(cargoType)) { countEachCargoType.Add(cargoType, 0); }
                countEachCargoType[cargoType]++;
            }

            return new PaymentCalculationData(countEachCarType, countEachCargoType);
        }

        public static List<CarsPerCargoType> ExtractCarsPerCargoType(List<Car> cars)
        {
            return ExtractCarsPerCargoType(cars, (from car in cars select car.CurrentCargoTypeInCar).ToList(), (from car in cars select car.LoadedCargoAmount).ToList());
        }

        public static List<CarsPerCargoType> ExtractCarsPerCargoType(List<Car> cars, List<CargoType> cargoTypes, List<float> cargoAmounts = null)
        {
            var hasCargoAmounts = cargoAmounts != null;
            if (cars.Count != cargoTypes.Count || (hasCargoAmounts && cars.Count != cargoAmounts.Count))
            {
                var messageBuilder = new StringBuilder("Expected lists of the same length, but got ");
                messageBuilder.Append($"List<Car> of length {cars.Count}");
                if (hasCargoAmounts)
                    messageBuilder.Append(", ");
                else
                    messageBuilder.Append(" and ");
                messageBuilder.Append($"List<CargoType> of length {cargoTypes.Count}");
                if (hasCargoAmounts)
                    messageBuilder.Append($", and List<float> of length {cargoAmounts.Count}");
                messageBuilder.Append(".");
                throw new ArgumentException(messageBuilder.ToString());
            }

            if (!hasCargoAmounts)
            {
                cargoAmounts = (from car in cars select 1f).ToList(); // this may break if the way cargo amounts work gets changed
            }

            var cargoTypeToCarAmountTuples = new Dictionary<CargoType, List<(Car, float)>>();

            for (var index = 0; index < cars.Count; ++index)
            {
                var car = cars[index];
                var cargoType = cargoTypes[index];
                var cargoAmount = cargoAmounts[index];
                if (!cargoTypeToCarAmountTuples.ContainsKey(cargoType))
                {
                    cargoTypeToCarAmountTuples.Add(cargoType, new List<(Car, float)>());
                }
                cargoTypeToCarAmountTuples[cargoType].Add((car, cargoAmount));
            }

            var carsPerCargoTypes = from kv in cargoTypeToCarAmountTuples
                                    let cargoType = kv.Key
                                    let carsForCargoType = (from tuple in kv.Value select tuple.Item1).ToList()
                                    let cargoAmount = (from tuple in kv.Value select tuple.Item2).Sum()
                                    select new CarsPerCargoType(cargoType, carsForCargoType, cargoAmount);

            return carsPerCargoTypes.ToList();
        }

        public static IEnumerable<TrainCar> ConvertLogicCarsToTrainCars(IEnumerable<Car> cars)
        {
            var trainCars = new List<TrainCar>();

            if (cars == null || cars.Count() == 0) { return trainCars; }

            var allTrainCars = CarSpawner.Instance.GetCars();
            var trainCarsByLogicCar = new Dictionary<Car, TrainCar>();

            foreach(var trainCar in allTrainCars)
            {
                trainCarsByLogicCar.Add(trainCar.logicCar, trainCar);
            }

            return from car in cars select trainCarsByLogicCar[car];
        }

        public static bool IsAnySpecialCar(TrainCarType carType)
        {
            return CarTypes.IsAnyLocomotiveOrTender(carType) || CarTypes.IsCaboose(carType);
        }
    }
}
