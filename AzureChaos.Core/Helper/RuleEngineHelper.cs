using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
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
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action, virtualMachineGroup.ToString(), entity.RowKey),
                SchedulerSessionId = sessionId,
                Rollbacked = false
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
                combinationKey = entity.AvailabilitySetId + Delimeters.Exclamatory + entity.FaultDomain?.ToString();
            }
            else
            {
                combinationKey = entity.AvailabilitySetId + Delimeters.At + entity.UpdateDomain?.ToString();
            }
            return new ScheduledRules(VirtualMachineGroup.AvailabilitySets.ToString(), entity.RowKey)
            {
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action, VirtualMachineGroup.AvailabilitySets.ToString(), entity.RowKey),
                SchedulerSessionId = sessionId,
                CombinationKey = combinationKey,
                Rollbacked = false
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
                ScheduledExecutionTime = executionTime,
                TriggerData = GetTriggerData(entity, action, VirtualMachineGroup.AvailabilityZones.ToString(), entity.RowKey),
                SchedulerSessionId = sessionId,
                CombinationKey = entity.RegionName + Delimeters.Exclamatory.ToString() + entity.AvailabilityZone,
                Rollbacked = false
            };
        }

        public static string GetTriggerData(CrawlerResponse crawlerResponse, ActionType action, string partitionKey, string rowKey)
        {
            InputObject triggerdata = new InputObject
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                Action = action,
                ResourceId = crawlerResponse.ResourceName,
                ResourceGroup = crawlerResponse.ResourceGroupName,
                VirtualMachineScaleSetId = crawlerResponse.PartitionKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash)
            };
            return JsonConvert.SerializeObject(triggerdata);
        }

        public static List<VirtualMachineGroup> GetEnabledChaosSet(AzureSettings azureSettings)
        {
            var enabledChaos = Mappings.GetEnabledChaos(azureSettings);

            var selectionQuery = TableQuery.GenerateFilterConditionForDate("ScheduledExecutionTime", QueryComparisons.GreaterThanOrEqual,
                DateTimeOffset.UtcNow.AddMinutes(-azureSettings.Chaos.SchedulerFrequency));
            var scheduledQuery = new TableQuery<ScheduledRules>().Where(selectionQuery);
            var executedResults = StorageAccountProvider.GetEntities(scheduledQuery, StorageTableNames.ScheduledRulesTableName);
            if (executedResults == null)
            {
                var chaos = enabledChaos.Where(x => x.Value);
                return chaos.Select(x => x.Key).ToList();
            }

            var scheduledRuleses = executedResults.ToList();
            var executedChaos = scheduledRuleses.Select(x => x.PartitionKey).Distinct().ToList();
            var excludedChaos = enabledChaos.Where(x => x.Value && !executedChaos.Contains(x.Key.ToString(), StringComparer.OrdinalIgnoreCase));
            return excludedChaos.Select(x => x.Key).ToList();
        }
    }
}