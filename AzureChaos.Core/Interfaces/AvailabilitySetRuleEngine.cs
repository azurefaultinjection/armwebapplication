using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using AzureChaos.Core.Providers;
using Fluent = Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AzureChaos.Core.Interfaces
{
    public class AvailabilitySetRuleEngine : IRuleEngine
    {
        private AzureSettings _azureSettings;

        public void CreateRule(AzureClient azureClient, TraceWriter log)
        {
            try
            {
                log.Info("Availability RuleEngine: Started the creating rules for the availability set.");
                _azureSettings = azureClient.AzureSettings;
                var random = new Random();
                var storageAccountProvider = new StorageAccountProvider();
                var storageAccount = storageAccountProvider.CreateOrGetStorageAccount(azureClient);
                //1) OpenSearch with Vm Count > 0
                var possibleAvailabilitySets = GetPossibleAvailabilitySets(storageAccountProvider, storageAccount);
                if (possibleAvailabilitySets == null)
                {
                    log.Info("Availability RuleEngine: Not found any Avilability sets with virtual machines");
                    return;
                }

                var recentlyExcludedAvailabilitySetDomainCombination = GetRecentlyExecutedAvailabilitySetDomainCombination(storageAccountProvider, storageAccount);
                var availableSetDomainOptions = possibleAvailabilitySets.Except(recentlyExcludedAvailabilitySetDomainCombination);
                if (availableSetDomainOptions == null)
                {
                    log.Info("Availability RuleEngine: Not found any Avilability sets after excluding the recent availabity sets");
                    return;
                }

                var availableSetDomainOptionsList = availableSetDomainOptions.ToList();
                var randomAvailabilitySetDomainCombination = availableSetDomainOptionsList[random.Next(0, availableSetDomainOptionsList.Count - 1)];
                var componentsInAvailabilitySetDomainCombination = randomAvailabilitySetDomainCombination.Split('@');
                var domainNumber = int.Parse(componentsInAvailabilitySetDomainCombination.Last());
                var availabilitySetId = componentsInAvailabilitySetDomainCombination.First();
                InsertVirtualMachineAvailabilitySetDomainResults(storageAccountProvider, storageAccount, availabilitySetId, domainNumber);
                log.Error("Availability RuleEngine: Completed creating rule engine");
            }
            catch (Exception ex)
            {
                log.Error("Availability RuleEngine: Exception thrown. ", ex);
            }
        }

        private void InsertVirtualMachineAvailabilitySetDomainResults(IStorageAccountProvider storageAccountProvider, CloudStorageAccount storageAccount, string availabilitySetId, int domainNumber)
        {
            var virtualMachineQuery = TableQuery.CombineFilters((TableQuery.GenerateFilterCondition("AvailableSetId",
                    QueryComparisons.Equal,
                    availabilitySetId)),
                TableOperators.And,
                _azureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled
                    ? TableQuery.GenerateFilterConditionForInt("FaultDomain",
                        QueryComparisons.Equal,
                        domainNumber)
                    : TableQuery.GenerateFilterConditionForInt("UpdateDomain",
                        QueryComparisons.Equal,
                        domainNumber));
            //TableQuery.GenerateFilterConditionForInt("AvailabilityZone", QueryComparisons.GreaterThanOrEqual, 0);
            var virtualMachinesTableQuery = new TableQuery<VirtualMachineCrawlerResponse>().Where(virtualMachineQuery);
            var crawledVirtualMachinesResults = storageAccountProvider.GetEntities(virtualMachinesTableQuery, storageAccount, _azureSettings.VirtualMachineCrawlerTableName);
            var virtualMachinesResults = crawledVirtualMachinesResults.ToList();
            if (!virtualMachinesResults.Any()) return;
            var domainFlag = !_azureSettings.Chaos.AvailabilitySetChaos.UpdateDomainEnabled;
            var scheduledRulesbatchOperation = VirtualMachineHelper.CreateScheduleEntityForAvailabilitySet(virtualMachinesResults.ToList(), _azureSettings.Chaos.SchedulerFrequency, domainFlag);
            if (scheduledRulesbatchOperation.Count <= 0) return;
            var table = storageAccountProvider.CreateOrGetTable(storageAccount, _azureSettings.ScheduledRulesTable);
            Fluent.Extensions.Synchronize(() => table.ExecuteBatchAsync(scheduledRulesbatchOperation));
        }

        private IEnumerable<string> GetRecentlyExecutedAvailabilitySetDomainCombination(IStorageAccountProvider storageAccountProvider, CloudStorageAccount storageAccount)
        {
            var recentlyExecutedAvailabilitySetDomainCombination = new List<string>();
            var possibleAvailabilitySetDomainCombinationVmCount = new Dictionary<string, int>();
            var meanTimeQuery = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime",
                QueryComparisons.GreaterThanOrEqual,
                DateTimeOffset.UtcNow.AddMinutes(-_azureSettings.Chaos.SchedulerFrequency));

            var recentlyExecutedAvailabilitySetDomainCombinationQuery = TableQuery.GenerateFilterCondition(
                "PartitionKey",
                QueryComparisons.Equal,
                VirtualMachineGroup.AvailabilitySets.ToString());

            var recentlyExecutedFinalAvailabilitySetDomainQuery = TableQuery.CombineFilters(meanTimeQuery,
                TableOperators.And,
                recentlyExecutedAvailabilitySetDomainCombinationQuery);

            var scheduledQuery = new TableQuery<ScheduledRules>().Where(recentlyExecutedFinalAvailabilitySetDomainQuery);
            var executedAvilabilitySetCombinationResults = storageAccountProvider.GetEntities(scheduledQuery, storageAccount, _azureSettings.ScheduledRulesTable);
            if (executedAvilabilitySetCombinationResults == null)
                return recentlyExecutedAvailabilitySetDomainCombination;

            foreach (var eachExecutedAvilabilitySetCombinationResults in executedAvilabilitySetCombinationResults)
            {
                if (_azureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled)
                {
                    if (!eachExecutedAvilabilitySetCombinationResults.CombinationKey.Contains("!")) continue;

                    if (possibleAvailabilitySetDomainCombinationVmCount.ContainsKey(eachExecutedAvilabilitySetCombinationResults.CombinationKey))
                    {
                        possibleAvailabilitySetDomainCombinationVmCount[eachExecutedAvilabilitySetCombinationResults.CombinationKey] += 1;
                    }
                    else
                    {
                        possibleAvailabilitySetDomainCombinationVmCount[eachExecutedAvilabilitySetCombinationResults.CombinationKey] = 1;
                    }
                }
                else
                {
                    if (!eachExecutedAvilabilitySetCombinationResults.CombinationKey.Contains("@")) continue;

                    if (possibleAvailabilitySetDomainCombinationVmCount.ContainsKey(eachExecutedAvilabilitySetCombinationResults.CombinationKey))
                    {
                        possibleAvailabilitySetDomainCombinationVmCount[eachExecutedAvilabilitySetCombinationResults.CombinationKey] += 1;
                    }
                    else
                    {
                        possibleAvailabilitySetDomainCombinationVmCount[eachExecutedAvilabilitySetCombinationResults.CombinationKey] = 1;
                    }
                }
            }
            recentlyExecutedAvailabilitySetDomainCombination = new List<string>(possibleAvailabilitySetDomainCombinationVmCount.Keys);
            return recentlyExecutedAvailabilitySetDomainCombination;
        }
        private List<string> GetPossibleAvailabilitySets(IStorageAccountProvider storageAccountProvider, CloudStorageAccount storageAccount)
        {
            var availabilitySetQuery = TableQuery.GenerateFilterConditionForBool("HasVirtualMachines", QueryComparisons.Equal,true);
            var availabilitySetTableQuery = new TableQuery<AvailabilitySetsCrawlerResponse>().Where(availabilitySetQuery);

            var crawledAvailabilitySetResults = storageAccountProvider.GetEntities(availabilitySetTableQuery, storageAccount, _azureSettings.AvailabilitySetCrawlerTableName);
            if (crawledAvailabilitySetResults == null)
            {
                return null;
            }

            var possibleAvailabilitySetDomainCombinationVmCount = new Dictionary<string, int>();
            var bootStrapQuery = string.Empty;
            var initialQuery = true;
            foreach (var eachAvailabilitySet in crawledAvailabilitySetResults)
            {
                if (initialQuery)
                {
                    bootStrapQuery = TableQuery.GenerateFilterCondition("AvailableSetId", QueryComparisons.Equal, ConvertToProperAvailableSetId(eachAvailabilitySet.RowKey));
                    initialQuery = false;
                }
                else
                {
                    var localAvailabilitySetQuery = TableQuery.GenerateFilterCondition("AvailableSetId", QueryComparisons.Equal, ConvertToProperAvailableSetId(eachAvailabilitySet.RowKey));
                    bootStrapQuery = TableQuery.CombineFilters(localAvailabilitySetQuery, TableOperators.Or, bootStrapQuery);
                }
            }

            var virtualMachineTableQuery = new TableQuery<VirtualMachineCrawlerResponse>().Where(bootStrapQuery);
            var crawledVirtualMachineResults = storageAccountProvider.GetEntities(virtualMachineTableQuery, storageAccount, _azureSettings.VirtualMachineCrawlerTableName);
            foreach (var eachVirtualMachine in crawledVirtualMachineResults)
            {
                string entryIntoPossibleAvailabilitySetDomainCombinationVmCount;
                if (_azureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled)
                {
                    entryIntoPossibleAvailabilitySetDomainCombinationVmCount = eachVirtualMachine.AvailableSetId + "@" + eachVirtualMachine.FaultDomain;
                }
                else
                {
                    entryIntoPossibleAvailabilitySetDomainCombinationVmCount = eachVirtualMachine.AvailableSetId + "@" + eachVirtualMachine.UpdateDomain;
                }

                if (possibleAvailabilitySetDomainCombinationVmCount.ContainsKey(entryIntoPossibleAvailabilitySetDomainCombinationVmCount))
                {
                    possibleAvailabilitySetDomainCombinationVmCount[entryIntoPossibleAvailabilitySetDomainCombinationVmCount] += 1;
                }
                else
                {
                    possibleAvailabilitySetDomainCombinationVmCount[entryIntoPossibleAvailabilitySetDomainCombinationVmCount] = 1;
                }
            }

            var possibleAvailableSets = new List<string>(possibleAvailabilitySetDomainCombinationVmCount.Keys);
            return possibleAvailableSets;
        }

        private static string ConvertToProperAvailableSetId(string rowKey)
        {
            var rowKeyChunks = rowKey.Split('!');
            return string.Join("/", rowKeyChunks).Replace(rowKeyChunks.Last(), rowKeyChunks.Last().ToUpper());
        }

        public Task CreateRuleAsync(AzureClient azureClient)
        {
            throw new NotImplementedException();
        }
    }
}
