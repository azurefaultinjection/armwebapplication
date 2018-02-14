﻿using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AzureChaos.Core.Helper
{
    public class RuleEngineHelper
    {
        public static ScheduledRules ConvertToScheduledRuleEntity<T>(T entity, string sessionId, ActionType action, DateTime executionTime, VirtualMachineGroup virtualMachineGroup) where T : CrawlerResponse
        {
            if (entity == null || !Mappings.FunctionNameMap.ContainsKey(virtualMachineGroup.ToString()))
            {
                return null;
            }

            return new ScheduledRules(virtualMachineGroup.ToString(), entity.RowKey)
            {
                ExecutorEndPoint = Mappings.FunctionNameMap[virtualMachineGroup.ToString()],
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action),
                SchedulerSessionId = sessionId
            };
        }

        public static ScheduledRules ConvertToScheduledRuleEntityForAvailabilitySet<T>(T entity, string sessionId, ActionType action, DateTime executionTime, bool domainFlage) where T : VirtualMachineCrawlerResponse
        {
            if (entity == null || !Mappings.FunctionNameMap.ContainsKey(VirtualMachineGroup.AvailabilitySets.ToString()))
            {
                return null;
            }
            string combinationKey;
            if (domainFlage)
            {
                combinationKey = entity.AvailableSetId + "!" + entity.FaultDomain.ToString();
            }
            else
            {
                combinationKey = entity.AvailableSetId + "@" + entity.UpdateDomain.ToString();
            }
            return new ScheduledRules(VirtualMachineGroup.AvailabilitySets.ToString(), entity.RowKey)
            {
                ExecutorEndPoint = Mappings.FunctionNameMap[VirtualMachineGroup.AvailabilitySets.ToString()],
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action),
                SchedulerSessionId = sessionId,
                CombinationKey = combinationKey
            };
        }

        public static ScheduledRules ConvertToScheduledRuleEntityForAvailabilityZone<T>(T entity, string sessionId, ActionType action, DateTime executionTime) where T : VirtualMachineCrawlerResponse
        {
            if (!Mappings.FunctionNameMap.ContainsKey(VirtualMachineGroup.AvailabilityZones.ToString()))
            {
                return null;
            }

            return new ScheduledRules(VirtualMachineGroup.AvailabilityZones.ToString(), entity.RowKey)
            {
                ExecutorEndPoint = Mappings.FunctionNameMap[VirtualMachineGroup.AvailabilityZones.ToString()],
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action),
                SchedulerSessionId = sessionId,
                CombinationKey = entity.RegionName + "!" + entity.AvailabilityZone
            };
        }

        public static string GetTriggerData(CrawlerResponse crawlerResponse, ActionType action)
        {
            InputObject triggerdata = new InputObject
            {
                Action = action,
                ResourceName = crawlerResponse.ResourceName,
                ResourceGroup = crawlerResponse.ResourceGroupName,
                ScalesetId = crawlerResponse.PartitionKey.Replace("!", "/")
            };
            return JsonConvert.SerializeObject(triggerdata);
        }

        public static List<VirtualMachineGroup> GetEnabledChaosSet(IStorageAccountProvider storageAccountProvider, AzureClient azureClient)
        {
            var storageAccount = storageAccountProvider.CreateOrGetStorageAccount(azureClient);
            var selectionQuery = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime", QueryComparisons.GreaterThanOrEqual,
                DateTimeOffset.UtcNow.AddMinutes(-azureClient.AzureSettings.Chaos.SchedulerFrequency));
            var scheduledQuery = new TableQuery<ScheduledRules>().Where(selectionQuery);
            var enabledChaos = Mappings.GetEnabledChaos(azureClient.AzureSettings);
            var executedResults = storageAccountProvider.GetEntities(scheduledQuery, storageAccount, azureClient.AzureSettings.ScheduledRulesTable);
            if (executedResults == null)
            {
                var chaos = enabledChaos.Where(x => x.Value);
                return chaos?.Select(x => x.Key).ToList();
            }

            var scheduledRuleses = executedResults.ToList();
            var executedChaos = scheduledRuleses.Select(x => x.PartitionKey).Distinct().ToList();
            var excludedChaos = enabledChaos.Where(x => x.Value && !executedChaos.Contains(x.Key.ToString(), StringComparer.OrdinalIgnoreCase));
            return excludedChaos?.Select(x => x.Key).ToList();
        }
    }
}
