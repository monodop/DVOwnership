using DV.Logic.Job;
using DV.RenderTextureSystem.BookletRender;
using Harmony12;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DVOwnership.Patches
{
    static class Helpers
    {
        public static IEnumerable<Car> _getCars(TaskData task)
        {
            if (task == null)
            {
                yield break;
            }

            if (task?.cars?.Count > 0 && task.cars != null)
            {
                foreach (var car in task.cars)
                {
                    yield return car;
                }
            }
            if (task?.nestedTasks?.Count > 0)
            {
                foreach (var subtask in task.nestedTasks)
                {
                    if (subtask != null)
                    {
                        foreach (var subtaskCar in _getCars(subtask.GetTaskData()))
                        {
                            yield return subtaskCar;
                        }
                    }
                }
            }
        }

        public static IEnumerable<CargoType> _getCargoTypePerCar(TaskData task)
        {
            if (task == null)
            {
                yield break;
            }

            if (task?.cars?.Count > 0 && task.cargoTypePerCar != null)
            {
                foreach (var cargoType in task.cargoTypePerCar)
                {
                    yield return cargoType;
                }
            }
            if (task?.nestedTasks?.Count > 0)
            {
                foreach (var subtask in task.nestedTasks)
                {
                    if (subtask != null)
                    {
                        foreach (var subtaskCargoType in _getCargoTypePerCar(subtask.GetTaskData()))
                        {
                            yield return subtaskCargoType;
                        }
                    }
                }
            }
        }

        public static string _getTrackId(TaskData task)
        {
            if (task.nestedTasks?.Count > 0)
            {
                return _getTrackId(task.nestedTasks.First().GetTaskData());
            }
            return task.destinationTrack.ID.FullDisplayID;
        }

        public static string GetTrainLength(IEnumerable<Car> cars)
        {
            return cars.Sum(c => c.length).ToString("F") + " m";
        }

        public static string GetTrainMass(IEnumerable<Car> cars, IEnumerable<CargoType> cargoTypePerCar)
        {
            var sum = cars.Zip(cargoTypePerCar, (car, cargo) => car.carOnlyMass + (car.capacity * CargoTypes.GetCargoUnitMass(cargo))).Sum();
            return (sum * 0.001f).ToString("F") + " t";
        }

        public static string GetTrainValue(IEnumerable<Car> cars, IEnumerable<CargoType> cargoTypePerCar)
        {
            var sum = cars.Zip(cargoTypePerCar, (car, cargo) => ResourceTypes.GetFullDamagePriceForCar(car.carType) + ResourceTypes.GetFullDamagePriceForCargo(cargo)).Sum();
            return "$" + (sum / 1000000f).ToString("F") + "m";
        }

        public static string GetTimeLimit(Job job)
        {
            return (job.TimeLimit > 0) ? (Mathf.FloorToInt(job.TimeLimit / 60f) + " min") : "No bonus";
        }

        public delegate StationInfo ExtractStationDelegate(string id);
        public static readonly ExtractStationDelegate ExtractStationFromId =
            AccessTools.Method(typeof(BookletCreator), "ExtractStationInfoWithYardID")?.CreateDelegate(typeof(ExtractStationDelegate)) as ExtractStationDelegate;

    }

    // BookletCreator.GetBookletTemplateData
    [HarmonyPatch(typeof(BookletCreator), "GetBookletTemplateData")]
    static class GetBookletTemplateData_Patches
    {
        static Color? _TRACK_COLOR = null;
        static Color TRACK_COLOR
        {
            get
            {
                if (!_TRACK_COLOR.HasValue)
                {
                    _TRACK_COLOR = AccessTools.Field(typeof(BookletCreator), "TRACK_COLOR")?.GetValue(null) as Color?;
                    if (!_TRACK_COLOR.HasValue)
                    {
                        //PassengerJobs.ModEntry.Logger.Error("Failed to get track color from BookletCreator");
                        return Color.white;
                    }
                }
                return _TRACK_COLOR.Value;
            }
        }

        static bool Prefix(Job job, ref List<TemplatePaperData> __result )
        {
            // TODO: add our own job type - for mod compatibility?
            if (job.jobType != JobType.ComplexTransport)
            {
                // We don't handle anything else
                return true;
            }

            __result = GetBookletData(job);
            return false;
        }

        static List<TemplatePaperData> GetBookletData(Job job)
        {
            var pages = new List<TemplatePaperData>();
            int pageNum = 1;
            int stepNum = 1;
            int totalPages = 6;

            var jobData = job.GetJobData();

            var superTask = jobData.FirstOrDefault();
            if (superTask == null)
                throw new Exception($"could not find super task {job.ID}");

            var nestedTasks = superTask.nestedTasks.Select(t => t.GetTaskData()).ToArray();

            var stageTask = nestedTasks[0];
            var loadTask = nestedTasks[1];
            var haulTask = nestedTasks[2];
            var unloadTask = nestedTasks[3];

            var originStation = Helpers.ExtractStationFromId(job.chainData.chainOriginYardId);
            var destinationStation = Helpers.ExtractStationFromId(job.chainData.chainDestinationYardId);

            // Cover Page
            pages.Add(new CoverPageTemplatePaperData(
                jobID: job.ID,
                jobType: "FREIGHT HAUL",
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));
            DVOwnership.LogDebug(() => "created cover page");

            // Overview Page
            pages.Add(new FrontPageTemplatePaperData(
                jobType: "FREIGHT HAUL",
                jobSubtype: "",
                jobId: job.ID,
                jobTypeColor: UnityEngine.Color.green,
                jobDescription: $"Load the cars at {originStation.Name}, deliver them to {destinationStation.Name}, and unload them.",
                requiredLicenses: job.requiredLicenses,
                distinctCargoTypes: haulTask.cargoTypePerCar.Distinct().ToList(),
                cargoTypePerCar: haulTask.cargoTypePerCar,
                singleStationName: "",
                singleStationType: "",
                singleStationBgColor: UnityEngine.Color.white,
                startStationName: originStation.Name,
                startStationType: originStation.Type,
                startStationBgColor: originStation.StationColor,
                endStationName: destinationStation.Name,
                endStationType: destinationStation.Type,
                endStationBgColor: destinationStation.StationColor,
                cars: Helpers._getCars(haulTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                trainLenght: Helpers.GetTrainLength(Helpers._getCars(haulTask)),
                trainMass: Helpers.GetTrainMass(Helpers._getCars(haulTask), Helpers._getCargoTypePerCar(haulTask)),
                trainValue: Helpers.GetTrainValue(Helpers._getCars(haulTask), Helpers._getCargoTypePerCar(haulTask)),
                timeBonus: Helpers.GetTimeLimit(job),
                payment: job.GetBasePaymentForTheJob().ToString(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            // Stage Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "COUPLE",
                taskDescription: "Couple following cars at station/track:",
                yardId: originStation.YardID,
                yardColor: originStation.StationColor,
                trackId: Helpers._getTrackId(stageTask),
                trackColor: TRACK_COLOR,
                stationName: "",
                stationType: "",
                stationColor: TemplatePaperData.NOT_USED_COLOR,
                cars: Helpers._getCars(stageTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: Helpers._getCargoTypePerCar(stageTask).ToList(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));
            DVOwnership.LogDebug(() => "created stage task page");

            // Load Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "LOAD",
                taskDescription: "Load cars on loading track:",
                yardId: originStation.YardID,
                yardColor: originStation.StationColor,
                trackId: Helpers._getTrackId(loadTask),
                trackColor: TRACK_COLOR,
                stationName: "",
                stationType: "",
                stationColor: TemplatePaperData.NOT_USED_COLOR,
                cars: Helpers._getCars(loadTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: Helpers._getCargoTypePerCar(loadTask).ToList(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));
            DVOwnership.LogDebug(() => "created load task page");

            // Transport Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "TRANSPORT",
                taskDescription: "Transport train the following location:",
                yardId: destinationStation.YardID,
                yardColor: destinationStation.StationColor,
                trackId: Helpers._getTrackId(haulTask),
                trackColor: TRACK_COLOR,
                stationName: destinationStation.Name,
                stationType: destinationStation.Type,
                stationColor: destinationStation.StationColor,
                cars: Helpers._getCars(haulTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: Helpers._getCargoTypePerCar(haulTask).ToList(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));
            DVOwnership.LogDebug(() => "created transport task page");

            // Unload Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "UNLOAD",
                taskDescription: $"Unload cars on the unloading track:",
                yardId: destinationStation.YardID,
                yardColor: destinationStation.StationColor,
                trackId: Helpers._getTrackId(unloadTask),
                trackColor: TRACK_COLOR,
                stationName: "",
                stationType: "",
                stationColor: TemplatePaperData.NOT_USED_COLOR,
                cars: Helpers._getCars(unloadTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: Helpers._getCargoTypePerCar(unloadTask).ToList(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));
            DVOwnership.LogDebug(() => "created unload task page");

            return pages;
        }
    }

    // BookletCreator.GetJobOverviewTemplateData()
    [HarmonyPatch(typeof(BookletCreator), "GetJobOverviewTemplateData")]
    [HarmonyPatch(new Type[] { typeof(Job) })]
    static class GetJobOverviewTemplateData_Patches
    {
        static bool Prefix(Job job, ref List<TemplatePaperData> __result)
        {
            // TODO: add our own job type - for mod compatibility?
            if (job.jobType != JobType.ComplexTransport)
            {
                // We don't handle anything else
                return true;
            }

            __result = GetBookletData(job);
            return false;
        }

        static List<TemplatePaperData> GetBookletData(Job job)
        {
            var pages = new List<TemplatePaperData>();
            int pageNum = 1;
            int totalPages = 1;

            var jobData = job.GetJobData();

            var superTask = jobData.FirstOrDefault();
            if (superTask == null)
                throw new Exception($"could not find super task {job.ID}");

            var nestedTasks = superTask.nestedTasks.Select(t => t.GetTaskData()).ToArray();

            var haulTask = nestedTasks[2];

            var originStation = Helpers.ExtractStationFromId(job.chainData.chainOriginYardId);
            var destinationStation = Helpers.ExtractStationFromId(job.chainData.chainDestinationYardId);

            // Overview Page
            pages.Add(new FrontPageTemplatePaperData(
                jobType: "FREIGHT HAUL",
                jobSubtype: "",
                jobId: job.ID,
                jobTypeColor: UnityEngine.Color.green,
                jobDescription: $"Load the cars at {originStation.Name}, deliver them to {destinationStation.Name}, and unload them.",
                requiredLicenses: job.requiredLicenses,
                distinctCargoTypes: haulTask.cargoTypePerCar.Distinct().ToList(),
                cargoTypePerCar: haulTask.cargoTypePerCar,
                singleStationName: "",
                singleStationType: "",
                singleStationBgColor: UnityEngine.Color.white,
                startStationName: originStation.Name,
                startStationType: originStation.Type,
                startStationBgColor: originStation.StationColor,
                endStationName: destinationStation.Name,
                endStationType: destinationStation.Type,
                endStationBgColor: destinationStation.StationColor,
                cars: Helpers._getCars(haulTask).Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                trainLenght: Helpers.GetTrainLength(Helpers._getCars(haulTask)),
                trainMass: Helpers.GetTrainMass(Helpers._getCars(haulTask), Helpers._getCargoTypePerCar(haulTask)),
                trainValue: Helpers.GetTrainValue(Helpers._getCars(haulTask), Helpers._getCargoTypePerCar(haulTask)),
                timeBonus: Helpers.GetTimeLimit(job),
                payment: job.GetBasePaymentForTheJob().ToString(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            return pages;
        }
    }
}
