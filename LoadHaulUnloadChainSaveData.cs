using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DVOwnership
{
    public class LoadHaulUnloadChainSaveData : JobChainSaveData
    {
        // TODO: handle multiple job subtypes?

        [JsonConstructor]
        public LoadHaulUnloadChainSaveData(JobDefinitionDataBase[] jobChainData, string[] trainCarGuids, bool jobTaken, TaskSaveData[] currentJobTaskData, string firstJobId)
            : base(jobChainData, trainCarGuids, jobTaken, currentJobTaskData, firstJobId)
        {

        }

        public LoadHaulUnloadChainSaveData(JobChainSaveData baseData)
            : base(baseData.jobChainData, baseData.trainCarGuids, baseData.jobTaken, baseData.currentJobTaskData, baseData.firstJobId)
        {

        }
    }
}
