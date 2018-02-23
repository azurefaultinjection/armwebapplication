using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Api
{
    public static class ApiList
    {
        [FunctionName("getsubscriptions")]
        public static HttpResponseMessage GetSubscriptions([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
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
            return req.CreateResponse(HttpStatusCode.OK, subscriptionList);
        }

        [FunctionName("getresourcegroups")]
        public static HttpResponseMessage GetResourceGroups([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
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
            return req.CreateResponse(HttpStatusCode.OK, resourceGroups);
        }

        [FunctionName("getactivities")]
        public static HttpResponseMessage GetActivities([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            var bodyContentTask = req.Content.ReadAsAsync<object>();

            // Get request body
            dynamic data = bodyContentTask.Result;
            var fromDate = data?.fromDate;
            var toDate = data?.toDate;
            if (!DateTimeOffset.TryParse(fromDate, out DateTimeOffset fromDateTimeOffset))
            {
                fromDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);
            }

            if (!DateTimeOffset.TryParse(fromDate, out DateTimeOffset toDateTimeOffset))
            {
                toDateTimeOffset = DateTimeOffset.UtcNow;
            }

            var entities =
                ResourceFilterHelper.QueryByFromToDate<EventActivity>(fromDateTimeOffset, toDateTimeOffset, "EntryDate", StorageTableNames.ActivityLogTableName);

            return req.CreateResponse(HttpStatusCode.OK, entities);
        }

        [FunctionName("getschedules")]
        public static async Task<HttpResponseMessage> GetSchedules([HttpTrigger(AuthorizationLevel.Function, "get")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // Get request body
            dynamic data = await req.Content.ReadAsAsync<object>();
            var fromDate = data?.fromDate;
            var toDate = data?.toDate;
            if (!DateTimeOffset.TryParse(fromDate, out DateTimeOffset fromDateTimeOffset))
            {
                fromDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);
            }

            if (!DateTimeOffset.TryParse(fromDate, out DateTimeOffset toDateTimeOffset))
            {
                toDateTimeOffset = DateTimeOffset.UtcNow;
            }

            var entities =
                ResourceFilterHelper.QueryByFromToDate<ScheduledRules>(fromDateTimeOffset, toDateTimeOffset, "ScheduledExecutionTime", StorageTableNames.ScheduledRulesTableName);

            return req.CreateResponse(HttpStatusCode.OK, entities);
        }
    }
}