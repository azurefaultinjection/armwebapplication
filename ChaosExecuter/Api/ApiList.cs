using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Api
{
    public static class ApiList
    {
        [FunctionName("getsubscriptions")]
        public static HttpResponseMessage GetSubscriptions([HttpTrigger(AuthorizationLevel.Anonymous, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            var origin = req.Headers.Contains("Origin") ? req.Headers.GetValues("Origin")?.FirstOrDefault() : null;
            var azureSettings = new AzureClient().AzureSettings;
            var subscriptionClient = AzureClient.GetSubscriptionClient(azureSettings.Client.ClientId,
                azureSettings.Client.ClientSecret,
                azureSettings.Client.TenantId);

            var subscriptionTask = subscriptionClient.Subscriptions.ListAsync();
            if (subscriptionTask.Result == null)
            {
                return req.CreateResponse(HttpStatusCode.NoContent, "Empty result");
            }

            var subscriptionList = subscriptionTask.Result.Select(x => x);

            //return response;
            var response = req.CreateResponse(HttpStatusCode.OK, subscriptionList);
            if (req.Headers.Contains("Origin"))
            {
                response = AttachCrossDomainHeader(response, origin);
            }
            return response;
        }

        [FunctionName("getresourcegroups")]
        public static HttpResponseMessage GetResourceGroups([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            var origin = req.Headers.Contains("Origin") ? req.Headers.GetValues("Origin")?.FirstOrDefault() : null;
            var azureSettings = new AzureClient().AzureSettings;
            var resourceManagementClient = AzureClient.GetResourceManagementClientClient(azureSettings.Client.ClientId,
                azureSettings.Client.ClientSecret,
                azureSettings.Client.TenantId, azureSettings.Client.SubscriptionId);
            var resourceGroupTask = resourceManagementClient.ResourceGroups.ListAsync();
            if (resourceGroupTask.Result == null)
            {
                return req.CreateResponse(HttpStatusCode.NoContent, "Empty result");
            }

            var resourceGroups = resourceGroupTask.Result.Select(x => x);
            var response = req.CreateResponse(HttpStatusCode.OK, resourceGroups);
            if (req.Headers.Contains("Origin"))
            {
                response = AttachCrossDomainHeader(response, origin);
            }
            return response;
        }

        [FunctionName("getactivities")]
        public static async Task<HttpResponseMessage> GetActivities([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();
            string fromDate = data?.fromDate;
            string toDate = data?.toDate;
            var entities = GetSchedulesByDate(fromDate, toDate);
            var result = entities.Select(ConvertToActivity);
            return req.CreateResponse(HttpStatusCode.OK, result);
        }

        [FunctionName("getschedules")]
        public static async Task<HttpResponseMessage> GetSchedules([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();
            string fromDate = data?.fromDate;
            string toDate = data?.toDate;
            var entities = GetSchedulesByDate(fromDate, toDate);
            var result = entities.Select(ConvertToSchedule);
            return req.CreateResponse(HttpStatusCode.OK, result);
        }

        private static Schedules ConvertToSchedule(ScheduledRules scheduledRule)
        {
            return new Schedules()
            {
                ResourceId = scheduledRule.RowKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash),
                ScheduledTime = scheduledRule.ScheduledExecutionTime.ToString(),
                ChaosOperation = scheduledRule.ChaosAction,
                IsRolbacked = scheduledRule.Rollbacked,
                Status = scheduledRule.ExecutionStatus
            };
        }

        private static Activities ConvertToActivity(ScheduledRules scheduledRule)
        {
            return new Activities()
            {
                ResourceId = scheduledRule.RowKey.Replace(Delimeters.Exclamatory, Delimeters.ForwardSlash),
                ChaosStartedTime = scheduledRule.ExecutionStartTime.ToString(),
                ChaosCompletedTime = scheduledRule.EventCompletedTime.ToString(),
                ChaosOperation = scheduledRule.ChaosAction,
                InitialState = scheduledRule.InitialState,
                FinalState = scheduledRule.FinalState,
                Status = scheduledRule.ExecutionStatus,
                Error = scheduledRule.Error,
                Warning = scheduledRule.Warning
            };
        }

        private static IEnumerable<ScheduledRules> GetSchedulesByDate(string fromDate, string toDate)
        {
            if (!DateTimeOffset.TryParse(fromDate, out var fromDateTimeOffset))
            {
                fromDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);
            }

            if (!DateTimeOffset.TryParse(toDate, out var toDateTimeOffset))
            {
                toDateTimeOffset = DateTimeOffset.UtcNow;
            }

            return ResourceFilterHelper.QueryByFromToDate<ScheduledRules>(fromDateTimeOffset,
                toDateTimeOffset,
                "ScheduledExecutionTime",
                StorageTableNames.ScheduledRulesTableName);
        }

        private static HttpResponseMessage AttachCrossDomainHeader(HttpResponseMessage response, string origin)
        {
            response.Headers.Add("Access-Control-Allow-Credentials", "true");
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            return response;
        }
    }
}