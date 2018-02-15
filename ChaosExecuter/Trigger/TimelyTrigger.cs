using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChaosExecuter.Trigger
{
    /// <summary>Scheduled trigger - will pick the lastest rules from the scheduled rules table and execute the executer if the execution time is near.</summary>
    public static class TimelyTrigger
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        // TODO will be adding the CRON expression from the config.
        /// <summary>Every 5 mints </summary>
        //[FunctionName("TimelyTrigger")]
        public static async void Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, [OrchestrationClient]
        DurableOrchestrationClient starter, TraceWriter log)
        {
            log.Info($"Timely trigger function execution started: {DateTime.UtcNow}");
            try
            {
                var scheduledRules = await GetScheduledRulesForExecution(log);
                if (scheduledRules == null)
                {
                    log.Info($"Timely trigger no entries to trigger");
                    return;
                }

                // Start the executers parallely
                await Task.WhenAll(GetListOfExecuters(scheduledRules, starter, log));
            }
            catch (Exception e)
            {
                log.Error($"Timely trigger function threw exception:", e, "TimelyTrigger");
            }
        }

        /// <summary>Get the list of the executer instances from the scheduled Rules data.</summary>
        /// <param name="scheduledRules">List of the scheduled Rules from the scheduled table.</param>
        /// <param name="starter">Durable Orchestration client instance, to start the executer function</param>
        /// <param name="log">Trace writer to log the information/warning/errors.</param>
        /// <returns>The list of task, which has the instances of the executers.</returns>
        private static List<Task> GetListOfExecuters(IEnumerable<ScheduledRules> scheduledRules, DurableOrchestrationClient starter, TraceWriter log)
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
                log.Info($"Timely trigger: invoking function: " + functionName);
                tasks.Add(starter.StartNewAsync(functionName, result.TriggerData));
            }

            return tasks;
        }

        /// <summary>Get the scheduled rules for the chaos execution.</summary>
        /// <returns></returns>
        private static async Task<IEnumerable<ScheduledRules>> GetScheduledRulesForExecution(TraceWriter log)
        {
            try
            {
                var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);

                var dateFilterByUtc = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime", QueryComparisons.GreaterThanOrEqual,
                    DateTimeOffset.UtcNow);

                var dateFilterByFrequency = TableQuery.GenerateFilterConditionForDate("scheduledExecutionTime", QueryComparisons.LessThanOrEqual,
                    DateTimeOffset.UtcNow.AddMinutes(AzureClient.AzureSettings.Chaos.TriggerFrequency));

                var filter = TableQuery.CombineFilters(dateFilterByUtc, TableOperators.And, dateFilterByFrequency);
                var scheduledQuery = new TableQuery<ScheduledRules>().Where(filter);

                return await StorageProvider.GetEntitiesAsync(scheduledQuery, storageAccount, AzureClient.AzureSettings.ScheduledRulesTable);
            }
            catch (Exception e)
            {
                log.Error($"TimerTrigger function threw exception", e, "GetScheduledRulesForExecution");
                throw;
            }
        }
    }
}
