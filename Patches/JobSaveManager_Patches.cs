using DV.Logic.Job;
using Harmony12;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace DVOwnership.Patches
{
    public class JobSaveManager_Patches
    {
        private static bool isSetup = false;

        public static void Setup()
        {
            if (isSetup)
            {
                DVOwnership.LogWarning("Trying to set up JobSaveManager patches, but they've already been set up!");
                return;
            }

            DVOwnership.Log("Setting up JobSaveManager patches.");

            isSetup = true;
            var JobSaveManager_GetYardTrackWithId = AccessTools.Method(typeof(JobSaveManager), "GetYardTrackWithId");
            var JobSaveManager_GetYardTrackWithId_Postfix = AccessTools.Method(typeof(JobSaveManager_Patches), nameof(GetYardTrackWithId_Postfix));
            DVOwnership.Patch(JobSaveManager_GetYardTrackWithId, postfix: new HarmonyMethod(JobSaveManager_GetYardTrackWithId_Postfix));

            var JobSaveManager_LoadJobChain = AccessTools.Method(typeof(JobSaveManager), "LoadJobChain");
            var JobSaveManager_LoadJobChain_Prefix = AccessTools.Method(typeof(JobSaveManager_Patches), nameof(LoadJobChain_Prefix));
            DVOwnership.Patch(JobSaveManager_LoadJobChain, prefix: new HarmonyMethod(JobSaveManager_LoadJobChain_Prefix));
        }

        static void GetYardTrackWithId_Postfix(string trackId, ref Track __result)
        {
            __result ??= SingletonBehaviour<CarsSaveManager>.Instance.OrderedRailtracks.Select(railTrack => railTrack.logicTrack).FirstOrDefault(logicTrack => logicTrack.ID.FullID == trackId);
        }

        private static Track GetYardTrackWithId(string trackId)
        {
            if (YardTracksOrganizer.Instance.yardTrackIdToTrack.TryGetValue(trackId, out Track track))
            {
                return track;
            }
            throw new System.Exception("missing yard with id " + trackId);
            //return null;
        }

        private delegate void InitJobBookletDelegate(Job job);
        private static readonly InitJobBookletDelegate InitializeCorrespondingJobBooklet =
            AccessTools.Method("JobSaveManager:InitializeCorrespondingJobBooklet")?
                .CreateDelegate(typeof(InitJobBookletDelegate), SingletonBehaviour<JobSaveManager>.Instance) as InitJobBookletDelegate;

        static bool LoadJobChain_Prefix(JobSaveManager __instance, JobChainSaveData chainSaveData, ref GameObject __result)
        {
            if (chainSaveData == null)
            {
                __result = null;
                return false;
            }

            if (chainSaveData.jobChainData.Length < 1)
            {
                return true;
            }

            if (!(chainSaveData is LoadHaulUnloadChainSaveData lhuData))
            {
                return true;
            }

            var chainController = CreateSavedJobChain(lhuData);
            if (chainController == null)
            {
                return true;
            }

            chainController.FinalizeSetupAndGenerateFirstJob(true);

            if (chainSaveData.jobTaken)
            {
                PlayerJobs.Instance.TakeJob(chainController.currentJobInChain, true);
                if (chainSaveData.currentJobTaskData != null)
                {
                    chainController.currentJobInChain.OverrideTasksStates(chainSaveData.currentJobTaskData);
                }
                else
                {
                    throw new System.Exception("Job from chain was taken, but there is no task data! Task data won't be loaded!");
                    // TODO: error
                }

                if (chainController.currentJobInChain == null)
                {
                    throw new System.Exception("current job in chain was null");
                }

                var jobBooklets = (from item in SingletonBehaviour<StorageController>.Instance.GetAllStorageItems()
                               where item.GetComponent<JobBooklet>() != null
                               select item into jbi
                               select jbi.GetComponent<JobBooklet>()).ToList();

                if (jobBooklets == null)
                {
                    throw new System.Exception("job booklets was null");
                }

                foreach (var jb in jobBooklets)
                {
                    if (jb == null)
                    {
                        throw new System.Exception("job booklet was null ");
                    }
                }
                int num = jobBooklets.FindIndex((JobBooklet jb) => jb.jobIdLoadedData == chainController.currentJobInChain.ID && !jb.HasJobAssigned());
                DVOwnership.LogDebug(() => "num: " + num);

                typeof(JobSaveManager)
                    .GetMethod("InitializeCorrespondingJobBooklet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(__instance, new object[] { chainController.currentJobInChain });
                //InitializeCorrespondingJobBooklet(chainController.currentJobInChain);
            }

            // TODO: log success
            __result = chainController.jobChainGO;
            return false;
        }

        private static List<TrainCar> GetTrainCarsFromCarGuids(ICollection<string> carGuids)
        {
            if (carGuids == null || carGuids.Count == 0)
            {
                //PrintError("carGuids are null or empty!");
                // TODO: error
                //return null;
                throw new System.Exception("carGuids are null or empty!");
            }

            var result = new List<TrainCar>();

            foreach (string guid in carGuids)
            {
                if (!SingletonBehaviour<IdGenerator>.Instance.carGuidToCar.TryGetValue(guid, out Car car) || car == null)
                {
                    //PrintError($"Couldn't find corresponding Car for carGuid:{guid}!");
                    // TODO: error
                    //return null;
                    throw new System.Exception($"Couldn't find corresponding Car for carGuid:{guid}!");
                }

                if (!SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar.TryGetValue(car, out TrainCar trainCar) || !(trainCar != null))
                {
                    //PrintError($"Couldn't find corresponding TrainCar for Car: {car.ID} with carGuid:{guid}!");
                    // TODO: error
                    //return null;
                    throw new System.Exception($"Couldn't find corresponding TrainCar for Car: {car.ID} with carGuid:{guid}!");
                }

                result.Add(trainCar);
            }

            return result;
        }
        private static List<Car> GetCarsFromCarGuids(ICollection<string> carGuids)
        {
            if (carGuids == null || carGuids.Count == 0)
            {
                //PrintError("carGuids are null or empty!");
                // TODO: error
                //return null;
                throw new System.Exception("carGuids are null or empty!");
            }

            var result = new List<Car>();

            foreach (string guid in carGuids)
            {
                if (!SingletonBehaviour<IdGenerator>.Instance.carGuidToCar.TryGetValue(guid, out Car car) || car == null)
                {
                    //PrintError($"Couldn't find corresponding Car for carGuid:{guid}!");
                    // TODO: error
                    //return null;
                    throw new System.Exception($"Couldn't find corresponding Car for carGuid:{guid}!");
                }
                result.Add(car);
            }

            return result;
        }

        private static List<CarsPerTrack> GetCarsPerTrackFromCarGuids(ICollection<CarGuidsPerTrackId> carGuids)
        {
            if (carGuids == null || carGuids.Count == 0)
            {
                //PrintError("carGuids are null or empty!");
                // TODO: error
                return null;
            }

            var result = new List<CarsPerTrack>();

            foreach (var guid in carGuids)
            {
                result.Add(new CarsPerTrack(GetYardTrackWithId(guid.trackId), GetCarsFromCarGuids(guid.carGuids)));
            }

            return result;
        }

        private static List<CarsPerCargoType> GetCarsPerCargoFromCarGuids(ICollection<CarGuidsPerCargo> carGuids)
        {
            if (carGuids == null || carGuids.Count == 0)
            {
                //PrintError("carGuids are null or empty!");
                // TODO: error
                return null;
            }

            var result = new List<CarsPerCargoType>();

            foreach (var guid in carGuids)
            {
                result.Add(new CarsPerCargoType(guid.cargo, GetCarsFromCarGuids(guid.carGuids), guid.totalCargoAmount));
            }

            return result;
        }

        static JobChainController CreateSavedJobChain(LoadHaulUnloadChainSaveData chainData)
        {
            if (InitializeCorrespondingJobBooklet == null)
            {
                // TODO: error
                throw new System.Exception("Failed to connect to JobSaveManager methods");
                //return null;
            }

            var trainCarsFromCarGuids = GetTrainCarsFromCarGuids(chainData.trainCarGuids);
            if (trainCarsFromCarGuids == null)
            {
                throw new System.Exception("Couldn't find trainCarsForJobChain with trainCarGuids from chainSaveData! Skipping load of this job chain!");
                // TODO: error
                //return null;
            }

            var jobChainGO = new GameObject();
            var chainController = new JobChainController(jobChainGO);
            chainController.trainCarsForJobChain = trainCarsFromCarGuids;

            if (chainData.jobChainData.Length == 0)
            {
                throw new System.Exception("job chain data was empty");
            }

            foreach (var jobData in chainData.jobChainData)
            {
                if (!(jobData is LoadHaulUnloadJobDefinitionData lhuData))
                {
                    throw new System.Exception("chain contains invalid job type");
                    // TODO: error
                    //return null;
                }

                var jobDefinition = PopulateLoadHaulUnloadJobDefinitionWithExistingCars(jobChainGO, lhuData);
                if (jobDefinition == null)
                {
                    throw new System.Exception("Failed to generate job definition from save data");
                    // TODO: error
                    //return null;
                }

                jobDefinition.ForceJobId(chainData.firstJobId);
                var stationsChainData = jobDefinition.chainData;

                chainController.AddJobDefinitionToChain(jobDefinition);
            }

            jobChainGO.name = ""; // TODO
            return chainController;
        }

        private static Station GetStationWithId(string stationId)
        {
            if (SingletonBehaviour<LogicController>.Exists &&
                SingletonBehaviour<LogicController>.Instance.YardIdToStationController.TryGetValue(stationId, out StationController stationController))
            {
                return stationController?.logicStation;
            }
            return null;
        }

        public static StaticLoadHaulUnloadJobDefinition PopulateLoadHaulUnloadJobDefinitionWithExistingCars(GameObject chainJobGO, LoadHaulUnloadJobDefinitionData data)
        {
            if (!(GetStationWithId(data.stationId) is Station logicStation))
            {
                // TODO: error
                throw new System.Exception("missing station");
                //return null;
            }

            if (data.timeLimitForJob < 0f || data.initialWage < 0 || string.IsNullOrEmpty(data.originStationId) || string.IsNullOrEmpty(data.destinationStationId))
            {
                // TODO: error
                throw new System.Exception("bad time, wage, or origin / destination station id");
                //return null;
            }

            if (!(GetStationWithId(data.originStationId) is Station originStation))
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing origin station");
            }

            if (!(GetStationWithId(data.destinationStationId) is Station destinationStation))
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing destination station");
            }

            if (!LicenseManager.IsValidForParsingToJobLicense(data.requiredLicenses))
            {
                // TODO: error
                //return null;
                throw new System.Exception("bad licenses");
            }

            if (string.IsNullOrEmpty(data.loadMachineId))
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing load machine id");
            }
            var loadMachine = originStation.yard.WarehouseMachines.FirstOrDefault(m => m.ID == data.loadMachineId);
            if (loadMachine == null)
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing load machine");
            }

            if (string.IsNullOrEmpty(data.unloadMachineId))
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing unload machine id");
            }
            var unloadMachine = destinationStation.yard.WarehouseMachines.FirstOrDefault(m => m.ID == data.unloadMachineId);
            if (unloadMachine == null)
            {
                // TODO: error
                //return null;
                throw new System.Exception("missing unload machine");
            }

            // TODO: validate rest of things

            return ProceduralJobGenerators.PopulateLoadHaulUnloadJobDefinitionWithExistingCars(
                chainJobGO,
                logicStation,
                GetCarsPerTrackFromCarGuids(data.carGuidsPerStartingTrackId),
                loadMachine,
                unloadMachine,
                GetCarsPerCargoFromCarGuids(data.carGuidsPerLoadCargo),
                data.timeLimitForJob,
                data.initialWage,
                new StationsChainData(data.originStationId, data.destinationStationId),
                (JobLicenses)data.requiredLicenses
            );
        }
    }


    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.GetJobChainSaveData))]
    static class JCC_GetJobChainSaveData_Patch
    {
        static void Postfix(JobChainController __instance, ref JobChainSaveData __result)
        {
            __result = new LoadHaulUnloadChainSaveData(__result);
        }
    }
}