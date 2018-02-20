using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    public static class VirtualMachineScaleSetCrawler
    {
        [FunctionName("VirtualMachineScaleSetCrawler")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

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
                var vmTable = StorageAccountProvider.CreateOrGetTableAsync(azureSettings.VirtualMachineCrawlerTableName);
                var scaleSetTable = StorageAccountProvider.CreateOrGetTableAsync(azureSettings.ScaleSetCrawlerTableName);

                await Task.WhenAll(vmTable, scaleSetTable);

                var batchTasks = new ConcurrentBag<Task>();
                // using parallel here to run all the resource groups parallelly, parallel is 10times faster than normal foreach.
                Parallel.ForEach(resourceGroups, resourceGroup =>
                {
                    try
                    {
                        var scaleSetsList = AzureClient.AzureInstance.VirtualMachineScaleSets
                            .ListByResourceGroup(resourceGroup.Name);
                        GetVirtualMachineAndScaleSetBatch(scaleSetsList, batchTasks, vmTable.Result,
                            scaleSetTable.Result, log);
                    }
                    catch (Exception e)
                    {
                        //  catch the error, to continue adding other entities to table
                        log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e, "GetScaleSetsForResourceGroups: for the resource group " + resourceGroup.Name);
                    }
                });

                // execute all batch operation as parallel
                Parallel.ForEach(batchTasks, new ParallelOptions { MaxDegreeOfParallelism = 20 }, (task) =>
                {
                    try
                    {
                        Task.WhenAll(task);
                    }
                    catch (Exception e)
                    {
                        log.Error($"timercrawlerforavailableset threw the exception on executing the batch operation ", e);
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
        /// <param name="batchTasks"></param>
        /// <param name="vmTable">Get the virtual machine table instance</param>
        /// <param name="scaleSetTable">Get the scale set table instance</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns></returns>
        private static void GetVirtualMachineAndScaleSetBatch(
            IEnumerable<IVirtualMachineScaleSet> scaleSets,
            ConcurrentBag<Task> batchTasks,
            CloudTable vmTable,
            CloudTable scaleSetTable,
            TraceWriter log)
        {
            if (scaleSets == null || vmTable == null || scaleSetTable == null)
            {
                return;
            }

            var scaleSetbatchOperation = new TableBatchOperation();
            // get the batch operation for all the scale sets and corresponding virtual machine instances
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
                    var vmBatchOperation = GetVirtualMachineBatchOperation(scaleSet.VirtualMachines.List(),
                        scaleSet.ResourceGroupName,
                        scaleSet.Id, zoneId);
                    if (vmBatchOperation != null && vmBatchOperation.Count > 0)
                    {
                        batchTasks.Add(vmTable.ExecuteBatchAsync(vmBatchOperation));
                    }
                }
                catch (Exception e)
                {
                    //  catch the error, to continue adding other entities to table
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                        "GetVirtualMachineAndScaleSetBatch for the scale set: " + scaleSet.Name);
                }
            });

            if (scaleSetbatchOperation.Count > 0)
            {
                batchTasks.Add(scaleSetTable.ExecuteBatchAsync(scaleSetbatchOperation));
            }
        }

        /// <summary>Insert the list of the scale set virtual machine instances into the table.</summary>
        /// <param name="virtualMachines">List of the virtual machines.</param>
        /// <param name="resourceGroupName">Resource group name of the scale set</param>
        /// <param name="scaleSetId">Id of the scale set</param>
        /// <param name="availabilityZone">Availability zone id of the scale set</param>
        /// <returns></returns>
        private static TableBatchOperation GetVirtualMachineBatchOperation(IEnumerable<IVirtualMachineScaleSetVM> virtualMachines,
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
