using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzureChaos.Core.Interfaces
{
    // TODO : Exception ending and logging
    /// <summary>Virtual machine rule engine will create the rules for the virtual machine based on the config settings and existing schedule/event tables.</summary>
    public class VirtualMachineRuleEngine : IRuleEngine
    {
        /// <summary>Create the virtual machine rules</summary>
        /// <param name="log"></param>
        public void CreateRule(TraceWriter log)
        {
            try
            {
                log.Info("VirtualMachine RuleEngine: Started the creating rules for the virtual machines.");
                var vmSets = GetRandomVmSet();
                if (vmSets == null)
                {
                    log.Info("VirtualMachine RuleEngine: No virtual machines found..");
                    return;
                }

                var azureSettings = AzureClient.AzureSettings;
                var table = StorageAccountProvider.CreateOrGetTable(azureSettings.ScheduledRulesTable);
                var count = VmCount(vmSets.Count);
                var tasks = new List<Task>();

                do
                {
                    var randomSets = vmSets.Take(count).ToList();
                    vmSets = vmSets.Except(randomSets).ToList();
                    var batchOperation = VirtualMachineHelper.CreateScheduleEntity(randomSets,
                        AzureClient.AzureSettings.Chaos.SchedulerFrequency,
                        VirtualMachineGroup.VirtualMachines);
                    if (batchOperation == null) continue;

                    tasks.Add(table.ExecuteBatchAsync(batchOperation));
                } while (vmSets.Any());

                Task.WhenAll(tasks);
                log.Info("VirtualMachine RuleEngine: Completed creating rule engine..");
            }
            catch (Exception ex)
            {
                log.Error("VirtualMachine RuleEngine: Exception thrown. ", ex);
            }
        }

        /// <summary>Get the list of virtual machines, based on the preconditioncheck on the schedule table and activity table.
        /// here precondion ==> get the virtual machines from the crawler which are not in the recent scheduled list and not in the recent activities.</summary>
        /// <returns></returns>
        private static IList<VirtualMachineCrawlerResponse> GetRandomVmSet()
        {
            var groupNameFilter = TableQuery.GenerateFilterCondition("VirtualMachineGroup",
                QueryComparisons.Equal,
                VirtualMachineGroup.VirtualMachines.ToString());
            var resultsSet = ResourceFilterHelper.QueryByMeanTime<VirtualMachineCrawlerResponse>(
                AzureClient.AzureSettings,
                AzureClient.AzureSettings.VirtualMachineCrawlerTableName, groupNameFilter);
            if (resultsSet == null || !resultsSet.Any())
            {
                return null;
            }

            // TODO need to combine the schedule and activity table
            var scheduleEntities = ResourceFilterHelper.QueryByMeanTime<ScheduledRules>(AzureClient.AzureSettings,
                AzureClient.AzureSettings.ScheduledRulesTable);
            var scheduleEntitiesResourceIds = scheduleEntities == null || !scheduleEntities.Any() ? new List<string>() :
                scheduleEntities.Select(x => x.RowKey.Replace("!", "/"));

            var activityEntities = ResourceFilterHelper.QueryByMeanTime<EventActivity>(AzureClient.AzureSettings,
                AzureClient.AzureSettings.ActivityLogTable);
            var activityEntitiesResourceIds = activityEntities == null || !activityEntities.Any() ? new List<string>() : activityEntities.Select(x => x.Id);

            var result = resultsSet.Where(x => !scheduleEntitiesResourceIds.Contains(x.Id) && !activityEntitiesResourceIds.Contains(x.Id));
            return result.ToList();
        }

        /// <summary>Get the virtual machine count based on the config percentage.</summary>
        /// <param name="totalCount">Total number of the virual machines.</param>
        /// <returns></returns>
        private static int VmCount(int totalCount)
        {
            var vmPercentage = AzureClient.AzureSettings?.Chaos?.VirtualMachineChaos?.PercentageTermination;
            return vmPercentage != null ? (int)(vmPercentage / 100 * totalCount) : totalCount;
        }
    }
}
