﻿using AzureChaos.Core.Constants;
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

namespace AzureChaos.Core.Interfaces
{
    public class AvailabilitySetRuleEngine : IRuleEngine
    {
        private AzureClient azureClient = new AzureClient();

        public void CreateRule(TraceWriter log)
        {
            try
            {
                log.Info("Availability RuleEngine: Started the creating rules for the availability set.");
                var random = new Random();
                //1) OpenSearch with Vm Count > 0
                var possibleAvailabilitySets = GetPossibleAvailabilitySets();
                if (possibleAvailabilitySets == null)
                {
                    log.Info("Availability RuleEngine: Not found any Avilability sets with virtual machines");
                    return;
                }

                var recentlyExcludedAvailabilitySetDomainCombination = GetRecentlyExecutedAvailabilitySetDomainCombination();
                var availableSetDomainOptions = possibleAvailabilitySets.Except(recentlyExcludedAvailabilitySetDomainCombination);
                var availableSetDomainOptionsList = availableSetDomainOptions.ToList();
                if (!availableSetDomainOptionsList.Any())
                {
                    return;
                }

                var randomAvailabilitySetDomainCombination = availableSetDomainOptionsList[random.Next(0, availableSetDomainOptionsList.Count - 1)];
                var componentsInAvailabilitySetDomainCombination = randomAvailabilitySetDomainCombination.Split('@');
                if (!componentsInAvailabilitySetDomainCombination.Any())
                {
                    return;
                }

                var domainNumber = int.Parse(componentsInAvailabilitySetDomainCombination.Last());
                var availabilitySetId = componentsInAvailabilitySetDomainCombination.First();
                InsertVirtualMachineAvailabilitySetDomainResults(availabilitySetId, domainNumber);
                log.Error("Availability RuleEngine: Completed creating rule engine");
            }
            catch (Exception ex)
            {
                log.Error("Availability RuleEngine: Exception thrown. ", ex);
            }
        }

        private void InsertVirtualMachineAvailabilitySetDomainResults(string availabilitySetId, int domainNumber)
        {
            var virtualMachineQuery = TableQuery.CombineFilters(TableQuery.GenerateFilterCondition("AvailableSetId",
                    QueryComparisons.Equal,
                    availabilitySetId),
                TableOperators.And,
                azureClient.AzureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled
                    ? TableQuery.GenerateFilterConditionForInt("FaultDomain",
                        QueryComparisons.Equal,
                        domainNumber)
                    : TableQuery.GenerateFilterConditionForInt("UpdateDomain",
                        QueryComparisons.Equal,
                        domainNumber));

            //TableQuery.GenerateFilterConditionForInt("AvailabilityZone", QueryComparisons.GreaterThanOrEqual, 0);
            var virtualMachinesTableQuery = new TableQuery<VirtualMachineCrawlerResponse>().Where(virtualMachineQuery);
            var crawledVirtualMachinesResults = StorageAccountProvider.GetEntities(virtualMachinesTableQuery, StorageTableNames.VirtualMachineCrawlerTableName);
            var virtualMachinesResults = crawledVirtualMachinesResults.ToList();
            if (!virtualMachinesResults.Any())
            {
                return;
            }

            var domainFlag = !azureClient.AzureSettings.Chaos.AvailabilitySetChaos.UpdateDomainEnabled;
            var scheduledRulesbatchOperation = VirtualMachineHelper.CreateScheduleEntityForAvailabilitySet(virtualMachinesResults, azureClient.AzureSettings.Chaos.SchedulerFrequency, domainFlag);
            if (scheduledRulesbatchOperation.Count <= 0)
            {
                return;
            }

            var table = StorageAccountProvider.CreateOrGetTable(StorageTableNames.ScheduledRulesTableName);
            table.ExecuteBatch(scheduledRulesbatchOperation);
        }

        private IEnumerable<string> GetRecentlyExecutedAvailabilitySetDomainCombination()
        {
            var recentlyExecutedAvailabilitySetDomainCombination = new List<string>();
            var possibleAvailabilitySetDomainCombinationVmCount = new Dictionary<string, int>();
            var meanTimeQuery = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime",
                QueryComparisons.GreaterThanOrEqual,
                DateTimeOffset.UtcNow.AddMinutes(-azureClient.AzureSettings.Chaos.SchedulerFrequency));

            var recentlyExecutedAvailabilitySetDomainCombinationQuery = TableQuery.GenerateFilterCondition(
                "PartitionKey",
                QueryComparisons.Equal,
                VirtualMachineGroup.AvailabilitySets.ToString());

            var recentlyExecutedFinalAvailabilitySetDomainQuery = TableQuery.CombineFilters(meanTimeQuery,
                TableOperators.And,
                recentlyExecutedAvailabilitySetDomainCombinationQuery);

            var scheduledQuery = new TableQuery<ScheduledRules>().Where(recentlyExecutedFinalAvailabilitySetDomainQuery);
            var executedAvilabilitySetCombinationResults = StorageAccountProvider.GetEntities(scheduledQuery, StorageTableNames.ScheduledRulesTableName);
            if (executedAvilabilitySetCombinationResults == null)
                return recentlyExecutedAvailabilitySetDomainCombination;

            foreach (var eachExecutedAvilabilitySetCombinationResults in executedAvilabilitySetCombinationResults)
            {
                if (azureClient.AzureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled)
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

        private List<string> GetPossibleAvailabilitySets()
        {
            var availabilitySetQuery = TableQuery.GenerateFilterConditionForBool("HasVirtualMachines", QueryComparisons.Equal, true);
            var availabilitySetTableQuery = new TableQuery<AvailabilitySetsCrawlerResponse>().Where(availabilitySetQuery);

            var crawledAvailabilitySetResults = StorageAccountProvider.GetEntities(availabilitySetTableQuery, StorageTableNames.AvailabilitySetCrawlerTableName);
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
            var crawledVirtualMachineResults = StorageAccountProvider.GetEntities(virtualMachineTableQuery, StorageTableNames.VirtualMachineCrawlerTableName);
            foreach (var eachVirtualMachine in crawledVirtualMachineResults)
            {
                string entryIntoPossibleAvailabilitySetDomainCombinationVmCount;
                if (azureClient.AzureSettings.Chaos.AvailabilitySetChaos.FaultDomainEnabled)
                {
                    entryIntoPossibleAvailabilitySetDomainCombinationVmCount = eachVirtualMachine.AvailabilitySetId + "@" + eachVirtualMachine.FaultDomain;
                }
                else
                {
                    entryIntoPossibleAvailabilitySetDomainCombinationVmCount = eachVirtualMachine.AvailabilitySetId + "@" + eachVirtualMachine.UpdateDomain;
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
    }
}