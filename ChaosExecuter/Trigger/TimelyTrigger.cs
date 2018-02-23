using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChaosExecuter.Trigger
{
    /// <summary>Scheduled trigger - will pick the lastest rules from the scheduled rules table and execute the executer
    /// if the execution time is near.</summary>
    public static class TimelyTrigger
    {
        // TODO will be adding the CRON expression from the config.
        /// <summary>Every 5 mints </summary>
       // [FunctionName("TimelyTrigger")]
        public static async Task Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, [OrchestrationClient]
        DurableOrchestrationClient starter, TraceWriter log)
        {
            log.Info($"Timely trigger function execution started: {DateTime.UtcNow}");
            try
            {
                var scheduledRules = GetScheduledRulesForExecution(log);
                var rollbackRules = GetScheduledRulesForRollback(log);
                if (scheduledRules == null)
                {
                    log.Info($"Timely trigger no entries to trigger");
                    return;
                }

                // Start the executers parallely
                await Task.WhenAll(GetListOfExecuters(scheduledRules, rollbackRules, starter, log));
            }
            catch (Exception e)
            {
                log.Error($"Timely trigger function threw exception:", e, "TimelyTrigger");
            }
        }

        /// <summary>Get the list of the executer instances from the scheduled Rules data.</summary>
        /// <param name="scheduledRules">List of the scheduled Rules from the scheduled table.</param>
        ///  /// <param name="rollbackRules">List of the rollback Rules from the scheduled table.</param>
        /// <param name="starter">Durable Orchestration client instance, to start the executer function</param>
        /// <param name="log">Trace writer to log the information/warning/errors.</param>
        /// <returns>The list of task, which has the instances of the executers.</returns>
        private static List<Task> GetListOfExecuters(IEnumerable<ScheduledRules> scheduledRules, IEnumerable<EventActivity> rollbackRules, DurableOrchestrationClient starter, TraceWriter log)
        {
            var tasks = new List<Task>();
            foreach (var result in scheduledRules)
            {
                var partitionKey = result.PartitionKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash);
                if (!Mappings.FunctionNameMap.ContainsKey(partitionKey))
                {
                    continue;
                }

                var functionName = Mappings.FunctionNameMap[partitionKey];
                log.Info($"Timely trigger: invoking function: {functionName}");
                tasks.Add(starter.StartNewAsync(functionName, result.TriggerData));
            }

            foreach (var result in rollbackRules)
            {
                var partitionKey = result.PartitionKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash);
                if (!Mappings.FunctionNameMap.ContainsKey(partitionKey))
                {
                    continue;
                }
                InputObject triggerdata = new InputObject
                {
                    Action = (ActionType)Enum.Parse(typeof(ActionType), result.InitialState.Split(Delimeters.ForwardSlash)[1]),
                    ResourceName = result.Resource,
                    ResourceGroup = result.PartitionKey,
                    VirtualMachineScaleSetId = result.Resource.Split(Delimeters.Underscore)[0]
                };
                var functionName = Mappings.FunctionNameMap[partitionKey];
                log.Info($"Timely trigger: invoking function: {functionName}");
                tasks.Add(starter.StartNewAsync(functionName, JsonConvert.SerializeObject(triggerdata)));
            }
            return tasks;
        }

        private static IEnumerable<EventActivity> GetScheduledRulesForRollback(TraceWriter log)
        {
            try
            {
                var azureSettings = new AzureClient().AzureSettings;
                var dateFilterByUtc = TableQuery.GenerateFilterConditionForDate("EventCompletedDate", QueryComparisons.LessThanOrEqual,
                    DateTimeOffset.UtcNow.AddMinutes(-azureSettings.Chaos.RollbackRunFrequency));
                var dateFilterByFrequency = TableQuery.GenerateFilterConditionForDate("EventCompletedDate", QueryComparisons.GreaterThanOrEqual,
                    DateTimeOffset.UtcNow.AddMinutes(-azureSettings.Chaos.RollbackRunFrequency - azureSettings.Chaos.TriggerFrequency));

                var filter = TableQuery.CombineFilters(dateFilterByUtc, TableOperators.And, dateFilterByFrequency);
                var scheduledQuery = new TableQuery<EventActivity>().Where(filter);

                return StorageAccountProvider.GetEntities(scheduledQuery, StorageTableNames.ScheduledRulesTableName);
            }
            catch (Exception e)
            {
                log.Error($"TimerTrigger function threw exception", e, "GetScheduledRulesForRollback");
                throw;
            }
        }

        /// <summary>Get the scheduled rules for the chaos execution.</summary>
        /// <returns></returns>
        private static IEnumerable<ScheduledRules> GetScheduledRulesForExecution(TraceWriter log)
        {
            try
            {
                var azureSettings = new AzureClient().AzureSettings;
                var dateFilterByUtc = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime", QueryComparisons.GreaterThanOrEqual,
                    DateTimeOffset.UtcNow);

                var dateFilterByFrequency = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime", QueryComparisons.LessThanOrEqual,
                    DateTimeOffset.UtcNow.AddMinutes(azureSettings.Chaos.TriggerFrequency));

                var filter = TableQuery.CombineFilters(dateFilterByUtc, TableOperators.And, dateFilterByFrequency);
                var scheduledQuery = new TableQuery<ScheduledRules>().Where(filter);

                return StorageAccountProvider.GetEntities(scheduledQuery, StorageTableNames.ScheduledRulesTableName);
            }
            catch (Exception e)
            {
                log.Error($"TimerTrigger function threw exception", e, "GetScheduledRulesForExecution");
                throw;
            }
        }
    }
}