using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    public static class AvailabilitySetCrawler
    {
        [FunctionName("AvailabilitySetCrawler")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]
            HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            var sw = Stopwatch.StartNew();//Recording
            var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription();
            if (resourceGroupList == null)
            {
                log.Info($"timercrawlerforavailabilitysets: no resource groups to crawler");
                return;
            }

            await InsertAvailabilitySets(resourceGroupList, log);
            log.Info("C# HTTP trigger function processed a request. time elapsed : " + sw.ElapsedMilliseconds);
        }

        /// <summary>1. Iterate the resource groups to get the availability sets for individual resource group.
        /// 2. Convert the List of availability sets into availability set entity and add them into the table batch operation.
        /// 3. Get the list of virtual machines, convert into entity and add them into the table batach operation
        /// 3. Everything will happen parallel using TPL Parallel.Foreach</summary>
        /// <param name="resourceGroupList">List of resource groups for the particular subscription.</param>
        /// <param name="log">Trace writer instance</param>
        private static async Task InsertAvailabilitySets(IEnumerable<IResourceGroup> resourceGroupList, TraceWriter log)
        {
            try
            {
                //Todo Remove the Async
                // create the virtual machine table and availability set table asynchronously and wait untill they are returned
                var virtualmachineCloudTable = StorageAccountProvider.CreateOrGetTableAsync(StorageTableNames.VirtualMachineCrawlerTableName);
                var availabilitySetCloudTable = StorageAccountProvider.CreateOrGetTableAsync(StorageTableNames.AvailabilitySetCrawlerTableName);
                await Task.WhenAny(availabilitySetCloudTable);

                // using concurrent bag to get the results from the parallel execution
                var virtualMachineConcurrentBag = new ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>>();
                var batchTasks = new ConcurrentBag<Task>();

                // get the availability set batch operation and vm list by availability sets
                SetTheVirtualMachinesAndAvailabilitySetBatchTask(resourceGroupList, virtualMachineConcurrentBag, batchTasks, availabilitySetCloudTable.Result, log);

                // get the virtual machine table batch operation parallely
                await Task.WhenAny(virtualmachineCloudTable);
                IncludeVirtualMachineTask(virtualMachineConcurrentBag, batchTasks, virtualmachineCloudTable.Result, log);

                // execute all batch operation as parallel
                Parallel.ForEach(batchTasks, new ParallelOptions { MaxDegreeOfParallelism = 20 }, (task) =>
                {
                    try
                    {
                        Task.WhenAll(task);
                    }
                    catch (Exception ex)
                    {
                        log.Error($"timercrawlerforavailableset threw the exception on executing the batch operation ", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                log.Error($"timercrawlerforavailableset threw the exception ", ex,
                    "GetAvilabilitySetsForResourceGroups");
            }
        }

        /// <summary>Include the virtual machine batch operation into existing batch opeation</summary>
        /// <param name="virtualMachineConcurrentBag"></param>
        /// <param name="batchTasks"></param>
        /// <param name="virtualMachineCloudTable"></param>
        /// <param name="log"></param>
        private static void IncludeVirtualMachineTask(ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>> virtualMachineConcurrentBag,
                                                      ConcurrentBag<Task> batchTasks, CloudTable virtualMachineCloudTable, TraceWriter log)
        {
            var groupsByVirtulaMachine = virtualMachineConcurrentBag.SelectMany(x => x);
            Parallel.ForEach(groupsByVirtulaMachine, groupItem =>
            {
                var virtualMachineBatchOperation = GetVirtualMachineBatchOperation(groupItem, log);
                if (virtualMachineBatchOperation != null && virtualMachineBatchOperation.Count > 0 && virtualMachineCloudTable != null)
                {
                    batchTasks.Add(virtualMachineCloudTable.ExecuteBatchAsync(virtualMachineBatchOperation));
                }
            });
        }

        /// <summary>Get the list of the availability sets by resource group.
        /// And get the virtual machine by resource group and the availability sets.
        /// And get the batch operation for the availability sets</summary>
        /// <param name="resourceGroupList"></param>
        /// <param name="virtualMachinesConcurrent"></param>
        /// <param name="batchTasks"></param>
        /// <param name="availabilitySetCloudTable"></param>
        /// <param name="log"></param>
        private static void SetTheVirtualMachinesAndAvailabilitySetBatchTask(IEnumerable<IResourceGroup> resourceGroupList,
            ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>> virtualMachinesConcurrent,
            ConcurrentBag<Task> batchTasks,
            CloudTable availabilitySetCloudTable,
            TraceWriter log)
        {
            Parallel.ForEach(resourceGroupList, eachResourceGroup =>
            {
                try
                {
                    var availabilitySetIds = new List<string>();
                    // get the availability sets by resource group
                    // get the availability sets batch operation and get the list of availability set ids
                    var availabilitySetbatchOperation =
                        GetAvailabilitySetBatchOperation(eachResourceGroup.Name, availabilitySetIds);

                    // add the batch operation into task list
                    if (availabilitySetbatchOperation.Count > 0 && availabilitySetCloudTable != null)
                    {
                        batchTasks.Add(availabilitySetCloudTable.ExecuteBatchAsync(availabilitySetbatchOperation));
                    }

                    // Get the virtual machines by resource group and by availability set ids
                    var virtualMachinesByAvailabilitySetId = GetVirtualMachineListByResourceGroup(eachResourceGroup.Name, availabilitySetIds);
                    if (virtualMachinesByAvailabilitySetId != null && virtualMachinesByAvailabilitySetId.Count > 0)
                    {
                        virtualMachinesConcurrent.Add(virtualMachinesByAvailabilitySetId);
                    }
                }
                catch (Exception e)
                {
                    log.Error($"timercrawlerforavailableset threw the exception ", e,
                        "for resource group: " + eachResourceGroup.Name);
                }
            });
        }

        /// <summary>Get the virtual machines by resource group and availability set ids.</summary>
        /// <param name="resourceGroupName"></param>
        /// <param name="availabilitySetIds"></param>
        /// <returns></returns>
        private static IList<IGrouping<string, IVirtualMachine>> GetVirtualMachineListByResourceGroup(string resourceGroupName, List<string> availabilitySetIds)
        {
            // Get the virtual machines by resource group
            var virtualMachinesList = AzureClient.AzureInstance.VirtualMachines.ListByResourceGroup(resourceGroupName).ToList();
            if (!virtualMachinesList.Any())
            {
                return null;
            }

            // Group the the virtual machine based on the availability set id
            var virtualMachinesByAvailabilitySetId = virtualMachinesList.Where(x => availabilitySetIds
                    .Contains(x.AvailabilitySetId, StringComparer.OrdinalIgnoreCase))
                .GroupBy(x => x.AvailabilitySetId, x => x).ToList();
            return virtualMachinesByAvailabilitySetId;
        }

        /// <summary>Get the availability set batch operation</summary>
        /// <param name="resourceGroupName">Resource group name to filter the availability set</param>
        /// <param name="availabilitySetIdList">List of availability set,
        /// which will be using to filter the virtual machine list by availability set ids</param>
        /// <returns></returns>
        private static TableBatchOperation GetAvailabilitySetBatchOperation(string resourceGroupName, List<string> availabilitySetIdList)
        {
            var availabilitySetsByResourceGroup = AzureClient.AzureInstance.AvailabilitySets.ListByResourceGroup(resourceGroupName);
            var availabilitySetBatchOperation = new TableBatchOperation();
            foreach (var eachAvailabilitySet in availabilitySetsByResourceGroup)
            {
                availabilitySetBatchOperation.InsertOrReplace(
                    ConvertToAvailabilitySetsCrawlerResponse(eachAvailabilitySet));
                availabilitySetIdList.Add(eachAvailabilitySet.Id);
            }

            return availabilitySetBatchOperation;
        }

        /// <summary>Get the virtual machine batch operation.</summary>
        /// <param name="virtualMachines">List of the virtual machines</param>
        /// <param name="log"></param>
        /// <returns></returns>
        private static TableBatchOperation GetVirtualMachineBatchOperation(IGrouping<string, IVirtualMachine> virtualMachines, TraceWriter log)
        {
            if (virtualMachines == null)
            {
                return null;
            }

            var virtualMachineTableBatchOperation = new TableBatchOperation();
            var partitionKey = virtualMachines.Key.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory);
            foreach (var eachVirtualMachine in virtualMachines)
            {
                try
                {
                    var virtualMachineEntity = VirtualMachineHelper.ConvertToVirtualMachineEntity(
                                                                    eachVirtualMachine, partitionKey,
                                                                    eachVirtualMachine.ResourceGroupName);
                    virtualMachineEntity.VirtualMachineGroup = VirtualMachineGroup.AvailabilitySets.ToString();
                    virtualMachineTableBatchOperation.InsertOrReplace(virtualMachineEntity);
                }
                catch (Exception ex)
                {
                    log.Error($"timercrawlerforavailableset threw the exception ", ex,
                        "GetVirtualMachineBatchOperation");
                }
            }

            return virtualMachineTableBatchOperation;
        }

        /// <summary>Convert the Availability Set instance to Availability set entity.</summary>
        /// <param name="availabilitySet">The scale set instance.</param>
        /// <returns></returns>
        private static AvailabilitySetsCrawlerResponse ConvertToAvailabilitySetsCrawlerResponse(IAvailabilitySet availabilitySet)
        {
            var availabilitySetEntity = new AvailabilitySetsCrawlerResponse(availabilitySet.ResourceGroupName, availabilitySet.Id.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory))
            {
                Id = availabilitySet.Id,
                RegionName = availabilitySet.RegionName,
                ResourceName = availabilitySet.Name,
                FaultDomainCount = availabilitySet.FaultDomainCount,
                UpdateDomainCount = availabilitySet.UpdateDomainCount,
            };

            if (availabilitySet.Inner?.VirtualMachines != null && availabilitySet.Inner.VirtualMachines.Count > 0)
            {
                availabilitySetEntity.HasVirtualMachines = true;
            }

            return availabilitySetEntity;
        }
    }
}