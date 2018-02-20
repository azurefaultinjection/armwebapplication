using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using AzureChaos.Core.Providers;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fluent = Microsoft.Azure.Management.ResourceManager.Fluent.Core;

namespace AzureChaos.Core.Interfaces
{
    //TODO : Exception ending and logging
    /// <summary>Scale set rule engine will create the rules for the virtual machine based on the config settings and existing schedule/event tables.</summary>
    public class ScaleSetRuleEngine : IRuleEngine
    {
        private static AzureSettings _azureSettings = AzureClient.AzureSettings;

        /// <summary>Create the rule for the virtual machine scale vms </summary>
        /// <param name="log"></param>
        public void CreateRule(TraceWriter log)
        {
            log.Info("Scaleset RuleEngine: Started the creating rules for the scale set.");
            try
            {
                var scaleSet = GetRandomScaleSet();
                if (scaleSet == null)
                {
                    log.Info("Scaleset RuleEngine: No scale set found with virtual machines.");
                    return;
                }

                var filteredVmSet = GetVirtualMachineSet(scaleSet.Id);
                if (filteredVmSet == null)
                {
                    log.Info("Scaleset RuleEngine: No virtual machines found for the scale set name: " + scaleSet.ResourceName);
                    return;
                }

                var table = StorageAccountProvider.CreateOrGetTable(_azureSettings.ScheduledRulesTable);
                var count = VmCount(filteredVmSet.Count, _azureSettings);
                var tasks = new List<Task>();

                do
                {
                    var randomSets = filteredVmSet.Take(count).ToList();
                    filteredVmSet = filteredVmSet.Except(randomSets).ToList();
                    var batchOperation = VirtualMachineHelper.CreateScheduleEntity(randomSets, _azureSettings.Chaos.SchedulerFrequency, VirtualMachineGroup.VirtualMachineScaleSets);

                    var operation = batchOperation;
                    tasks.Add(table.ExecuteBatchAsync(operation));
                } while (filteredVmSet.Any());

                Task.WhenAll(tasks);
                log.Info("Scaleset RuleEngine: Completed creating rule engine.");
            }
            catch (Exception ex)
            {
                log.Error("Scaleset RuleEngine: Exception thrown. ", ex);
            }
        }

        /// <summary>Pick the random scale set.</summary>
        /// <returns></returns>
        private static VirtualMachineScaleSetCrawlerResponse GetRandomScaleSet()
        {
            var filter = TableQuery.GenerateFilterConditionForBool("HasVirtualMachines", QueryComparisons.Equal, true);
            var resultsSet = ResourceFilterHelper.QueryByMeanTime<VirtualMachineScaleSetCrawlerResponse>(_azureSettings,
               _azureSettings.ScaleSetCrawlerTableName, filter);

            if (resultsSet == null || !resultsSet.Any())
            {
                return null;
            }

            var random = new Random();
            var randomScaleSetIndex = random.Next(0, resultsSet.Count);
            return resultsSet.ToArray()[randomScaleSetIndex];
        }

        /// <summary>Get the list of virtual machines, based on the precondition check on the schedule table and activity table.
        /// here precondion ==> get the virtual machines from the crawler which are not in the recent scheduled list and not in the recent activities.</summary>
        /// <param name="scaleSetId">scale set id to filter the virtual machines.</param>
        /// <returns></returns>
        private static IList<VirtualMachineCrawlerResponse> GetVirtualMachineSet(string scaleSetId)
        {
            var partitionKey = scaleSetId.Replace('/', '!');
            var groupNameFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);
            var resultsSet = ResourceFilterHelper.QueryByMeanTime<VirtualMachineCrawlerResponse>(_azureSettings,
                _azureSettings.VirtualMachineCrawlerTableName, groupNameFilter);
            if (resultsSet == null || !resultsSet.Any())
            {
                return null;
            }

            // TODO combine the schedule and activity table
            var scheduleEntities = ResourceFilterHelper.QueryByMeanTime<ScheduledRules>(
                _azureSettings,
                _azureSettings.ScheduledRulesTable);

            var scheduleEntitiesResourceIds = scheduleEntities == null || !scheduleEntities.Any()
                ? new List<string>()
                : scheduleEntities.Select(x => x.RowKey.Replace("!",
                    "/"));

            var activityEntities = ResourceFilterHelper.QueryByMeanTime<EventActivity>(
                _azureSettings,
                _azureSettings.ActivityLogTable);

            var activityEntitiesResourceIds = activityEntities == null || !activityEntities.Any()
                ? new List<string>()
                : activityEntities.Select(x => x.Id);

            var result = resultsSet.Where(x =>
                !scheduleEntitiesResourceIds.Contains(x.Id) && !activityEntitiesResourceIds.Contains(x.Id));
            return result.ToList();
        }

        /// <summary>Get the virtual machine count based on the config percentage.</summary>
        /// <param name="totalCount">Total number of the virual machines.</param>
        /// <param name="azureSettings">Azure configuration</param>
        /// <returns></returns>
        private static int VmCount(int totalCount, AzureSettings azureSettings)
        {
            var vmPercentage = azureSettings?.Chaos?.ScaleSetChaos?.PercentageTermination;

            return vmPercentage == null ? totalCount : (int)(vmPercentage / 100 * totalCount);
        }
    }
}