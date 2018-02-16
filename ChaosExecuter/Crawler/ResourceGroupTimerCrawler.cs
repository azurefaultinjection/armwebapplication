using AzureChaos.Core.Entity;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    public static class ResourceGroupTimerCrawler
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        [FunctionName("timercrawlerresourcegroups")]
        public static async Task Run([TimerTrigger("0 */2 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerresourcegroups executed at: {DateTime.UtcNow}");
            var azureSettings = AzureClient.AzureSettings;
            try
            {
                var resourceGroups = ResourceGroupHelper.GetResourceGroupsInSubscription(AzureClient.AzureInstance, azureSettings);
                await InsertOrReplaceResourceGroupsAsync(resourceGroups, log, azureSettings.ResourceGroupCrawlerTableName);
            }
            catch (Exception ex)
            {
                log.Error($"timercrawlerresourcegroups threw exception ", ex, "timercrawlerresourcegroups");
            }
        }

        private static async Task InsertOrReplaceResourceGroupsAsync(List<IResourceGroup> resourceGroups, TraceWriter log, string resourceGroupCrawlerTableName)
        {
            var batchOperation = new TableBatchOperation();
            Parallel.ForEach(resourceGroups, resourceGroup =>
            {
                var resourceGroupCrawlerResponseEntity = new ResourceGroupCrawlerResponse("", resourceGroup.Name);
                try
                {
                    resourceGroupCrawlerResponseEntity.ResourceGroupId = resourceGroup.Id;
                    resourceGroupCrawlerResponseEntity.RegionName = resourceGroup.RegionName;
                    batchOperation.InsertOrReplace(resourceGroupCrawlerResponseEntity);
                }
                catch (Exception ex)
                {
                    resourceGroupCrawlerResponseEntity.Error = ex.Message;
                    log.Error($"timercrawlerresourcegroups threw exception ", ex, "timercrawlerresourcegroups");
                }
            });

            var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);
            if (batchOperation.Count > 0)
            {
                try
                {
                    var table = await StorageProvider.CreateOrGetTableAsync(storageAccount,
                        resourceGroupCrawlerTableName);
                    await table.ExecuteBatchAsync(batchOperation);
                }
                catch (Exception ex)
                {
                    log.Error($"timercrawlerresourcegroups threw exception ", ex, "timercrawlerresourcegroups");
                }
            }
        }
    }
}