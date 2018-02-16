using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    /// <summary>Crawl the scale set and scalet set virtual machine instances from the resource groups which are specified in the configuration file. </summary>
    public static class VirtualMachineScaleSetTimerCrawler
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        // TODO: need to read the crawler timer from the configuration.
        [FunctionName("timercrawlerforvirtualmachinescaleset")]
        public static async Task Run([TimerTrigger("0 */2 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerforvirtualmachinescaleset executed at: {DateTime.UtcNow}");

            var azureSettings = AzureClient.AzureSettings;
            var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription(AzureClient.AzureInstance, azureSettings);
            if (resourceGroupList == null)
            {
                log.Info($"timercrawlerforvirtualmachinescaleset: no resource groups to crawl");
                return;
            }

            await GetScaleSetsForResourceGroupsAsync(resourceGroupList, log, AzureClient.AzureSettings);
        }

        /// <summary>1. Iterate the resource groups to get the scale sets for individual resource group.
        /// 2. Convert the List of scale sets into scale set entity and add them into the table batch operation.
        /// 3. Get the list of virtual machine instances, convert into entity and them into the table batach operation
        /// 3. Execute all the task parallely</summary>
        /// <param name="resourceGroups">List of resource groups for the particular subscription.</param>
        /// <param name="log">Trace writer instance</param>
        /// <param name="azureSettings">Azure settings configuration to get the table name of scale set and virtual machine.</param>
        private static async Task GetScaleSetsForResourceGroupsAsync(IEnumerable<IResourceGroup> resourceGroups,
            TraceWriter log, AzureSettings azureSettings)
        {
            try
            {
                var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);
                var vmTable = StorageProvider.CreateOrGetTableAsync(storageAccount, azureSettings.VirtualMachineCrawlerTableName);
                var scaleSetTable = StorageProvider.CreateOrGetTableAsync(storageAccount, azureSettings.ScaleSetCrawlerTableName);

                await Task.WhenAll(vmTable, scaleSetTable);

                // using parallel here to run all the resource groups parallelly, parallel is 10times faster than normal foreach.
                Parallel.ForEach(resourceGroups, async resourceGroup =>
                {
                    try
                    {
                        var scaleSetsList = await AzureClient.AzureInstance.VirtualMachineScaleSets.ListByResourceGroupAsync(resourceGroup.Name);
                        var tasks = InsertOrReplaceScaleSetEntitiesIntoTable(scaleSetsList, vmTable.Result,
                            scaleSetTable.Result, log);
                        if (tasks != null)
                        {
                            await Task.WhenAll(tasks);
                        }

                    }
                    catch (Exception e)
                    {
                        //  catch the error, to continue adding other entities to table
                        log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e, "GetScaleSetsForResourceGroups: for the resource group " + resourceGroup.Name);
                    }
                });

            }
            catch (Exception ex)
            {
                log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", ex, "GetScaleSetsForResourceGroups");
            }
        }

        /// <summary>1. Get the List of scale sets for the resource group.
        /// 2. Get all the virtual machines from the each scale set and them into batch operation
        /// 3. Combine all the tasks and return the list of tasks.</summary>
        /// <param name="scaleSets">List of scale sets for the resource group</param>
        /// <param name="vmTable">Get the virtual machine table instance</param>
        /// <param name="scaleSetTable">Get the scale set table instance</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns></returns>
        private static List<Task> InsertOrReplaceScaleSetEntitiesIntoTable(
            IPagedCollection<IVirtualMachineScaleSet> scaleSets,
            CloudTable vmTable,
            CloudTable scaleSetTable,
            TraceWriter log)
        {
            if (scaleSets == null || vmTable == null || scaleSetTable == null)
            {
                return null;
            }

            var tasks = new List<Task>();
            var scaleSetbatchOperation = new TableBatchOperation();

            Parallel.ForEach(scaleSets, scaleSet =>
            {
                try
                {
                    scaleSetbatchOperation.InsertOrReplace(ConvertToVmScaleSetCrawlerResponse(scaleSet));
                    var availabilityZone = scaleSet.AvailabilityZones?.FirstOrDefault()?.Value;
                    int? zoneId = null;
                    if (!string.IsNullOrWhiteSpace(availabilityZone))
                    {
                        zoneId = int.Parse(availabilityZone);
                    }

                    // get the scale set instances
                    var vmBatchOperation = InsertOrReplaceVmInstancesIntoTable(scaleSet.VirtualMachines.List(),
                        scaleSet.ResourceGroupName,
                        scaleSet.Id, zoneId);
                    if (vmBatchOperation != null && vmBatchOperation.Count > 0)
                    {
                        tasks.Add(vmTable.ExecuteBatchAsync(vmBatchOperation));
                    }
                }
                catch (Exception e)
                {
                    //  catch the error, to continue adding other entities to table
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                        "InsertOrReplaceScaleSetEntitiesIntoTable for the scale set: " + scaleSet.Name);
                }
            });

            if (scaleSetbatchOperation.Count > 0)
            {
                tasks.Add(scaleSetTable.ExecuteBatchAsync(scaleSetbatchOperation));
            }

            return tasks;
        }

        /// <summary>Insert the list of the scale set virtual machine instances into the table.</summary>
        /// <param name="virtualMachines">List of the virtual machines.</param>
        /// <param name="resourceGroupName">Resource group name of the scale set</param>
        /// <param name="scaleSetId">Id of the scale set</param>
        /// <param name="availabilityZone">Availability zone id of the scale set</param>
        /// <returns></returns>
        private static TableBatchOperation InsertOrReplaceVmInstancesIntoTable(IEnumerable<IVirtualMachineScaleSetVM> virtualMachines,
            string resourceGroupName,
            string scaleSetId,
            int? availabilityZone)
        {
            if (virtualMachines == null)
            {
                return null;
            }

            var vmbatchOperation = new TableBatchOperation();
            foreach (var instance in virtualMachines)
            {
                // Azure table doesnot allow partition key  with forward slash
                var partitionKey = scaleSetId.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory);
                vmbatchOperation.InsertOrReplace(VirtualMachineHelper.ConvertToVirtualMachineEntity(instance,
                    resourceGroupName,
                    scaleSetId, partitionKey, availabilityZone, VirtualMachineGroup.ScaleSets.ToString()));
            }

            return vmbatchOperation;
        }

        /// <summary>Convert the virtual machine scale set instance to scale set entity.</summary>
        /// <param name="scaleSet">The scale set instance.</param>
        /// <returns></returns>
        private static VmScaleSetCrawlerResponse ConvertToVmScaleSetCrawlerResponse(IVirtualMachineScaleSet scaleSet)
        {
            var scaleSetEntity = new VmScaleSetCrawlerResponse(scaleSet.ResourceGroupName, scaleSet.Id.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory))
            {
                ResourceName = scaleSet.Name,
                ResourceType = scaleSet.Type,
                EntryInsertionTime = DateTime.UtcNow,
                ResourceGroupName = scaleSet.ResourceGroupName,
                RegionName = scaleSet.RegionName,
                Id = scaleSet.Id
            };
            if (scaleSet.VirtualMachines != null && scaleSet.VirtualMachines.List().Any())
            {
                scaleSetEntity.HasVirtualMachines = true;
            }

            return scaleSetEntity;
        }
    }
}
