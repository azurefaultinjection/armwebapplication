using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    /// <summary>Crawl the scale set and scalet set virtual machine instances from the resource groups which are specified in the configuration file. </summary>
    public static class VirtualMachineScaleSetTimerCrawler
    {
        // TODO: need to read the crawler timer from the configuration.
        [FunctionName("timercrawlerforvirtualmachinescaleset")]
        public static async Task Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerforvirtualmachinescaleset executed at: {DateTime.UtcNow}");
            var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription();
            if (resourceGroupList == null)
            {
                log.Info($"timercrawlerforvirtualmachinescaleset: no resource groups to crawl");
                return;
            }

            await GetScaleSetsForResourceGroupsAsync(resourceGroupList, log);
        }

        /// <summary>1. Iterate the resource groups to get the scale sets for individual resource group.
        /// 2. Convert the List of scale sets into scale set entity and add them into the table batch operation.
        /// 3. Get the list of virtual machine instances, convert into entity and them into the table batach operation
        /// 3. Execute all the task parallely</summary>
        /// <param name="resourceGroups">List of resource groups for the particular subscription.</param>
        /// <param name="log">Trace writer instance</param>
        private static async Task GetScaleSetsForResourceGroupsAsync(IEnumerable<IResourceGroup> resourceGroups,
                                                                     TraceWriter log)
        {
            try
            {
                var virtualMachineCloudTable = StorageAccountProvider.CreateOrGetTableAsync(StorageTableNames.VirtualMachineCrawlerTableName);
                var virtualMachineScaleSetTable = StorageAccountProvider.CreateOrGetTableAsync(StorageTableNames.VirtualMachinesScaleSetCrawlerTableName);

                await Task.WhenAll(virtualMachineCloudTable, virtualMachineScaleSetTable);

                var batchTasks = new ConcurrentBag<Task>();
                // using parallel here to run all the resource groups parallelly, parallel is 10times faster than normal foreach.
                Parallel.ForEach(resourceGroups, eachResourceGroup =>
                {
                    try
                    {
                        var virtualMachineScaleSetsList = AzureClient.AzureInstance.VirtualMachineScaleSets
                                                          .ListByResourceGroup(eachResourceGroup.Name);
                        GetVirtualMachineAndScaleSetBatch(virtualMachineScaleSetsList, batchTasks,
                                                          virtualMachineCloudTable.Result,
                                                          virtualMachineScaleSetTable.Result, log);
                    }
                    catch (Exception e)
                    {
                        //  catch the error, to continue adding other entities to table
                        log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e, "GetScaleSetsForResourceGroups: for the resource group " + eachResourceGroup.Name);
                    }
                });

                // execute all batch operation as parallel
                Parallel.ForEach(batchTasks, new ParallelOptions { MaxDegreeOfParallelism = 20 }, (eachTask) =>
                {
                    try
                    {
                        Task.WhenAll(eachTask);
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
        /// <param name="virtualMachineScaleSets">List of scale sets for the resource group</param>
        /// <param name="batchTasks"></param>
        /// <param name="virtualMachineCloudTable">Get the virtual machine table instance</param>
        /// <param name="virtualMachineScaleSetCloudTable">Get the scale set table instance</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns></returns>
        private static void GetVirtualMachineAndScaleSetBatch(IEnumerable<IVirtualMachineScaleSet> virtualMachineScaleSets,
                                                              ConcurrentBag<Task> batchTasks, CloudTable virtualMachineCloudTable,
                                                              CloudTable virtualMachineScaleSetCloudTable, TraceWriter log)
        {
            if (virtualMachineScaleSets == null || virtualMachineCloudTable == null || virtualMachineScaleSetCloudTable == null)
            {
                return;
            }

            var virtualMachineScleSetTableBatchOperation = new TableBatchOperation();
            // get the batch operation for all the scale sets and corresponding virtual machine instances
            Parallel.ForEach(virtualMachineScaleSets, eachVirtualMachineScaleSet =>
            {
                try
                {
                    virtualMachineScleSetTableBatchOperation.InsertOrReplace(ConvertToVirtualMachineScaleSetCrawlerResponse(eachVirtualMachineScaleSet));

                    var availabilityZone = eachVirtualMachineScaleSet.AvailabilityZones?.FirstOrDefault()?.Value;
                    int? zoneId = null;
                    if (!string.IsNullOrWhiteSpace(availabilityZone))
                    {
                        zoneId = int.Parse(availabilityZone);
                    }

                    // get the scale set instances
                    var virtualMachinesBatchOperation = GetVirtualMachineBatchOperation(eachVirtualMachineScaleSet.VirtualMachines.List(),
                                                                                        eachVirtualMachineScaleSet.ResourceGroupName,
                                                                                        eachVirtualMachineScaleSet.Id, zoneId);
                    if (virtualMachinesBatchOperation != null && virtualMachinesBatchOperation.Count > 0)
                    {
                        batchTasks.Add(virtualMachineCloudTable.ExecuteBatchAsync(virtualMachinesBatchOperation));
                    }
                }
                catch (Exception e)
                {
                    //  catch the error, to continue adding other entities to table
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                        "GetVirtualMachineAndScaleSetBatch for the scale set: " + eachVirtualMachineScaleSet.Name);
                }
            });

            if (virtualMachineScleSetTableBatchOperation.Count > 0)
            {
                batchTasks.Add(virtualMachineScaleSetCloudTable.ExecuteBatchAsync(virtualMachineScleSetTableBatchOperation));
            }
        }

        /// <summary>Insert the list of the scale set virtual machine instances into the table.</summary>
        /// <param name="virtualMachines">List of the virtual machines.</param>
        /// <param name="resourceGroupName">Resource group name of the scale set</param>
        /// <param name="scaleSetId">Id of the scale set</param>
        /// <param name="availabilityZone">Availability zone id of the scale set</param>
        /// <returns></returns>
        private static TableBatchOperation GetVirtualMachineBatchOperation(IEnumerable<IVirtualMachineScaleSetVM> virtualMachines,
                                                                           string resourceGroupName, string scaleSetId,
                                                                           int? availabilityZone)
        {
            if (virtualMachines == null)
            {
                return null;
            }

            var virtualMachineBatchOperation = new TableBatchOperation();
            foreach (var eachVirtualMachine in virtualMachines)
            {
                // Azure table doesnot allow partition key  with forward slash
                var partitionKey = scaleSetId.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory);
                virtualMachineBatchOperation.InsertOrReplace(VirtualMachineHelper.ConvertToVirtualMachineEntity(eachVirtualMachine,
                                                                                  resourceGroupName, scaleSetId, partitionKey,
                                                                                  availabilityZone, VirtualMachineGroup.VirtualMachineScaleSets.ToString()));
            }

            return virtualMachineBatchOperation;
        }

        /// <summary>Convert the virtual machine scale set instance to scale set entity.</summary>
        /// <param name="virtualMachineScaleSet">The scale set instance.</param>
        /// <returns></returns>
        private static VirtualMachineScaleSetCrawlerResponse ConvertToVirtualMachineScaleSetCrawlerResponse(IVirtualMachineScaleSet virtualMachineScaleSet)
        {
            var virtualMachineScaleSetEntity = new VirtualMachineScaleSetCrawlerResponse(virtualMachineScaleSet.ResourceGroupName, virtualMachineScaleSet.Id.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory))
            {
                ResourceName = virtualMachineScaleSet.Name,
                ResourceType = virtualMachineScaleSet.Type,
                RegionName = virtualMachineScaleSet.RegionName,
                Id = virtualMachineScaleSet.Id
            };
            if (virtualMachineScaleSet.VirtualMachines != null && virtualMachineScaleSet.VirtualMachines.List().Any())
            {
                virtualMachineScaleSetEntity.HasVirtualMachines = true;
            }

            return virtualMachineScaleSetEntity;
        }
    }
}