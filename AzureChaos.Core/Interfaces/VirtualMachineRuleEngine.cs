using AzureChaos.Core.Constants;
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
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.WindowsAzure.Storage.Table.Protocol;

namespace AzureChaos.Core.Interfaces
{
    // TODO : Exception ending and logging
    /// <summary>Virtual machine rule engine will create the rules for the virtual machine based on the config settings and existing schedule/event tables.</summary>
    public class VirtualMachineRuleEngine : IRuleEngine
    {
        private AzureClient azureClient = new AzureClient();

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

                var table = StorageAccountProvider.CreateOrGetTable(StorageTableNames.ScheduledRulesTableName);
                var count = VmCount(vmSets.Count);
                var tasks = new List<Task>();

                do
                {
                    var randomSets = vmSets.Take(count).ToList();
                    vmSets = vmSets.Except(randomSets).ToList();
                    for (var i = 0;
                        i < randomSets.Count;
                        i += TableConstants.TableServiceBatchMaximumOperations)
                    {
                        var batchItems = randomSets.Skip(i)
                            .Take(TableConstants.TableServiceBatchMaximumOperations).ToList();

                        var batchOperation = VirtualMachineHelper.CreateScheduleEntity(batchItems,
                            azureClient.AzureSettings.Chaos.SchedulerFrequency,
                            VirtualMachineGroup.VirtualMachines);
                        if (batchOperation == null) continue;

                        tasks.Add(table.ExecuteBatchAsync(batchOperation));
                    }
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
        private IList<VirtualMachineCrawlerResponse> GetRandomVmSet()
        {
            var groupNameFilter = TableQuery.GenerateFilterCondition("VirtualMachineGroup",
                QueryComparisons.Equal,
                VirtualMachineGroup.VirtualMachines.ToString());
            var resultsSet = ResourceFilterHelper.QueryCrawlerResponseByMeanTime<VirtualMachineCrawlerResponse>(
                azureClient.AzureSettings,
                StorageTableNames.VirtualMachineCrawlerTableName, groupNameFilter);
            resultsSet = resultsSet.Where(x => PowerState.Parse(x.State) == PowerState.Running).ToList();
            if (!resultsSet.Any())
            {
                return null;
            }

            // TODO need to combine the schedule and activity table
            var scheduleEntities = ResourceFilterHelper.QuerySchedulesByMeanTime<ScheduledRules>(azureClient.AzureSettings,
                StorageTableNames.ScheduledRulesTableName);
            var scheduleEntitiesResourceIds = scheduleEntities == null || !scheduleEntities.Any() ? new List<string>() :
                scheduleEntities.Select(x => x.RowKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash));

            var result = resultsSet.Where(x => !scheduleEntitiesResourceIds.Contains(x.Id));
            return result.ToList();
        }

        /// <summary>Get the virtual machine count based on the config percentage.</summary>
        /// <param name="totalCount">Total number of the virual machines.</param>
        /// <returns></returns>
        private int VmCount(int totalCount)
        {
            var vmPercentage = azureClient.AzureSettings?.Chaos?.VirtualMachineChaos?.PercentageTermination;
            return vmPercentage != null ? (int)(vmPercentage / 100 * totalCount) : totalCount;
        }
    }
}
