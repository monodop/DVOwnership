using DV.Logic.Job;
using DV.RenderTextureSystem.BookletRender;
using Harmony12;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DVOwnership.Patches
{
    // BookletCreator.GetBookletTemplateData
    [HarmonyPatch(typeof(BookletCreator), "GetBookletTemplateData")]
    static class GetBookletTemplateData_Patches
    {
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

        delegate StationInfo ExtractStationDelegate(string id);
        static readonly ExtractStationDelegate ExtractStationFromId =
            AccessTools.Method(typeof(BookletCreator), "ExtractStationInfoWithYardID")?.CreateDelegate(typeof(ExtractStationDelegate)) as ExtractStationDelegate;

        static List<TemplatePaperData> GetBookletData(Job job)
        {
            var pages = new List<TemplatePaperData>();
            int pageNum = 0;
            int stepNum = 0;
            int totalPages = 5;

            var jobData = job.GetJobData();

            var superTask = jobData.FirstOrDefault();
            if (superTask != null)
                return pages;

            var nestedTasks = superTask.nestedTasks.Select(t => t.GetTaskData()).ToArray();

            var stageTask = nestedTasks[0];
            var loadTask = nestedTasks[1];
            var haulTask = nestedTasks[2];
            var unloadTask = nestedTasks[3];

            var originStation = ExtractStationFromId(job.chainData.chainOriginYardId);
            var destinationStation = ExtractStationFromId(job.chainData.chainDestinationYardId);

            // Cover Page
            pages.Add(new CoverPageTemplatePaperData(
                jobID: job.ID,
                jobType: "Load, Deliver, Unload",
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            // Stage Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "STAGE",
                taskDescription: "Stage cars on loading track:",
                yardId: originStation.YardID,
                yardColor: originStation.StationColor,
                trackId: stageTask.destinationTrack.ID.FullDisplayID,
                trackColor: originStation.StationColor,
                stationName: originStation.Name,
                stationType: originStation.Type,
                stationColor: originStation.StationColor,
                cars: stageTask.cars.Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: stageTask.cargoTypePerCar,
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            // Load Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "LOAD",
                taskDescription: "Load cars on loading track:",
                yardId: originStation.YardID,
                yardColor: originStation.StationColor,
                trackId: loadTask.destinationTrack.ID.FullDisplayID,
                trackColor: originStation.StationColor,
                stationName: originStation.Name,
                stationType: originStation.Type,
                stationColor: originStation.StationColor,
                cars: loadTask.cars.Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: loadTask.cargoTypePerCar,
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            // Transport Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "TRANSPORT",
                taskDescription: $"Transport cars to {destinationStation.Name}:",
                yardId: destinationStation.YardID,
                yardColor: destinationStation.StationColor,
                trackId: haulTask.destinationTrack.ID.FullDisplayID,
                trackColor: destinationStation.StationColor,
                stationName: destinationStation.Name,
                stationType: destinationStation.Type,
                stationColor: destinationStation.StationColor,
                cars: haulTask.cars.Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: haulTask.cargoTypePerCar,
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            // Unload Task Page
            pages.Add(new TaskTemplatePaperData(
                stepNum: (stepNum++).ToString(),
                taskType: "UNLOAD",
                taskDescription: $"Unload cars on the unloading track:",
                yardId: destinationStation.YardID,
                yardColor: destinationStation.StationColor,
                trackId: unloadTask.destinationTrack.ID.FullDisplayID,
                trackColor: destinationStation.StationColor,
                stationName: destinationStation.Name,
                stationType: destinationStation.Type,
                stationColor: destinationStation.StationColor,
                cars: unloadTask.cars.Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                cargoTypePerCar: unloadTask.cargoTypePerCar,
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

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

        delegate StationInfo ExtractStationDelegate(string id);
        static readonly ExtractStationDelegate ExtractStationFromId =
            AccessTools.Method(typeof(BookletCreator), "ExtractStationInfoWithYardID")?.CreateDelegate(typeof(ExtractStationDelegate)) as ExtractStationDelegate;

        static List<TemplatePaperData> GetBookletData(Job job)
        {
            var pages = new List<TemplatePaperData>();
            int pageNum = 0;
            int totalPages = 1;

            var jobData = job.GetJobData();

            var superTask = jobData.FirstOrDefault();
            if (superTask != null)
                throw new Exception($"could not find super task {job.ID}");

            var nestedTasks = superTask.nestedTasks.Select(t => t.GetTaskData()).ToArray();

            var stageTask = nestedTasks[0];
            var loadTask = nestedTasks[1];
            var haulTask = nestedTasks[2];
            var unloadTask = nestedTasks[3];

            var originStation = ExtractStationFromId(job.chainData.chainOriginYardId);
            var destinationStation = ExtractStationFromId(job.chainData.chainDestinationYardId);

            // Overview Page
            pages.Add(new FrontPageTemplatePaperData(
                jobType: "Load, Deliver, Unload",
                jobSubtype: "",
                jobId: job.ID,
                jobTypeColor: UnityEngine.Color.green,
                jobDescription: $"Load the cars at {originStation.Name}, deliver them to {destinationStation.Name}, and unload them.",
                requiredLicenses: job.requiredLicenses,
                distinctCargoTypes: stageTask.cargoTypePerCar.Distinct().ToList(),
                cargoTypePerCar: stageTask.cargoTypePerCar,
                singleStationName: "",
                singleStationType: "",
                singleStationBgColor: UnityEngine.Color.white,
                startStationName: originStation.Name,
                startStationType: originStation.Type,
                startStationBgColor: originStation.StationColor,
                endStationName: destinationStation.Name,
                endStationType: destinationStation.Type,
                endStationBgColor: destinationStation.StationColor,
                cars: stageTask.cars.Select(c => new Tuple<TrainCarType, string>(c.carType, c.ID)).ToList(),
                trainLenght: stageTask.cars.Sum(c => c.length).ToString(),
                trainMass: (stageTask.cars.Sum(c => c.carOnlyMass) + haulTask.cargoTypePerCar.Sum(c => CargoTypes.GetCargoMass(c, 1))).ToString(),
                trainValue: "TODO",
                timeBonus: job.GetBonusPaymentForTheJob().ToString(),
                payment: job.GetBasePaymentForTheJob().ToString(),
                pageNumber: (pageNum++).ToString(),
                totalPages: totalPages.ToString()
            ));

            return pages;
        }
    }
}
