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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    public static class AvailabilitySetCrawler
    {
        private static readonly AzureSettings _azureSettings = AzureClient.AzureSettings;

        [FunctionName("AvailabilitySetCrawler")]
        public static async Task Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]
            HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            var sw = Stopwatch.StartNew();
            var resourceGroupList = ResourceGroupHelper.
                GetResourceGroupsInSubscription(AzureClient.AzureInstance, AzureClient.AzureSettings);
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
        private static async Task InsertAvailabilitySets(IEnumerable<IResourceGroup> resourceGroupList,
            TraceWriter log)
        {
            try
            {
                var azureSettings = AzureClient.AzureSettings;

                // create the virtual machine table and availability set table asynchronously
                var vmTable = StorageAccountProvider.CreateOrGetTableAsync(
                    azureSettings.VirtualMachineCrawlerTableName);
                var availabilitySetTable = StorageAccountProvider.CreateOrGetTableAsync(
                    azureSettings.AvailabilitySetCrawlerTableName);

                await Task.WhenAll(vmTable, availabilitySetTable);

                // using concurrent bag to get the results from the parallel execution
                var vmConcurrent = new ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>>();
                var batchTasks = new ConcurrentBag<Task>();

                // get the availability set batch operation and vm list by availability sets
                SetTheVirtualMachinesAndBatchTask(resourceGroupList, vmConcurrent, batchTasks,
                    availabilitySetTable.Result, log);

                // get the virtual machine table batch operation parallely
                IncludeVirtualMachineTask(vmConcurrent, batchTasks, vmTable.Result, log);

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
                log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", ex,
                    "GetScaleSetsForResourceGroups");
            }
        }

        /// <summary>Include the virtual machine batch operation into existing batch opeation</summary>
        /// <param name="vmConcurrent"></param>
        /// <param name="batchTasks"></param>
        /// <param name="vmTable"></param>
        /// <param name="log"></param>
        private static void IncludeVirtualMachineTask(ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>> vmConcurrent,
            ConcurrentBag<Task> batchTasks,
            CloudTable vmTable,
            TraceWriter log)
        {
            var gr = vmConcurrent.SelectMany(x => x);
            Parallel.ForEach(gr, groupItem =>
            {
                var vmBatchOperation = GetVmBatchOperation(groupItem, log);
                if (vmBatchOperation != null && vmBatchOperation.Count > 0 && vmTable != null)
                {
                    batchTasks.Add(vmTable.ExecuteBatchAsync(vmBatchOperation));
                }
            });
        }

        /// <summary>Get the list of the availability sets by resource group.
        /// And get the virtual machine by resource group and the availability sets.
        /// And get the batch operation for the availability sets</summary>
        /// <param name="resourceGroupList"></param>
        /// <param name="vmConcurrent"></param>
        /// <param name="batchTasks"></param>
        /// <param name="availabilitySetTable"></param>
        /// <param name="log"></param>
        private static void SetTheVirtualMachinesAndBatchTask(IEnumerable<IResourceGroup> resourceGroupList,
            ConcurrentBag<IEnumerable<IGrouping<string, IVirtualMachine>>> vmConcurrent,
            ConcurrentBag<Task> batchTasks,
            CloudTable availabilitySetTable,
            TraceWriter log)
        {
            Parallel.ForEach(resourceGroupList, resourceGroup =>
            {
                try
                {
                    var availabilitySetIds = new List<string>();
                    // get the availability sets by resource group
                    // get the availability sets batch operation and get the list of availability set ids
                    var availabilitySetbatchOperation =
                        GetAvailabilitySetBatchOperation(resourceGroup.Name, availabilitySetIds);

                    // add the batch operation into task list
                    if (availabilitySetbatchOperation.Count > 0 && availabilitySetTable != null)
                    {
                        batchTasks.Add(
                            availabilitySetTable.ExecuteBatchAsync(availabilitySetbatchOperation));
                    }

                    // Get the virtual machines by resource group and by availability set ids
                    var vmsByAvailabilitySetId = GetVirtualMachineList(resourceGroup.Name, availabilitySetIds);
                    if (vmsByAvailabilitySetId !=null && vmsByAvailabilitySetId.Count > 0)
                    {
                        vmConcurrent.Add(vmsByAvailabilitySetId);
                    }
                }
                catch (Exception e)
                {
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                        "for resource group: " + resourceGroup.Name);
                }
            });
        }

        /// <summary>Get the virtual machines by resource group and availability set ids.</summary>
        /// <param name="resourceGroupName"></param>
        /// <param name="availabilitySetIds"></param>
        /// <returns></returns>
        private static IList<IGrouping<string, IVirtualMachine>> GetVirtualMachineList(string resourceGroupName, List<string> availabilitySetIds)
        {
            // Get the virtual machines by resource group
            var vmList = AzureClient.AzureInstance.VirtualMachines
                .ListByResourceGroup(resourceGroupName).ToList();
            if (!vmList.Any())
            {
                return null;
            }

            // Group the the virtual machine based on the availability set id 
            var vmsByAvailabilitySetId = vmList.Where(x => availabilitySetIds
                    .Contains(x.AvailabilitySetId, StringComparer.OrdinalIgnoreCase))
                .GroupBy(x => x.AvailabilitySetId, x => x).ToList();
            return vmsByAvailabilitySetId;
        }

        /// <summary>Get the availability set batch operation</summary>
        /// <param name="resourceGroupName">Resource group name to filter the availability set</param>
        /// <param name="asIdList">List of availability set, 
        /// which will be using to filter the virtual machine list by availability set ids</param>
        /// <returns></returns>
        private static TableBatchOperation GetAvailabilitySetBatchOperation(string resourceGroupName,
            List<string> asIdList)
        {
            var asSetsByRg =
                AzureClient.AzureInstance.AvailabilitySets.ListByResourceGroup(resourceGroupName);

            var availabilitySetbatchOperation = new TableBatchOperation();
            foreach (var availabilitySet in asSetsByRg)
            {
                availabilitySetbatchOperation.InsertOrReplace(
                    ConvertToAvailabilitySetsCrawlerResponse(availabilitySet));
                asIdList.Add(availabilitySet.Id);
            }

            return availabilitySetbatchOperation;
        }

        /// <summary>Get the virtual machine batch operation.</summary>
        /// <param name="virtualMachines">List of the virtual machines</param>
        /// <param name="log"></param>
        /// <returns></returns>
        private static TableBatchOperation GetVmBatchOperation(
            IGrouping<string, IVirtualMachine> virtualMachines,
             TraceWriter log)
        {
            if (virtualMachines == null)
            {
                return null;
            }

            var vmbatchOperation = new TableBatchOperation();
            var partitionKey = virtualMachines.Key.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory);
            foreach (var virtualMachine in virtualMachines)
            {
                try
                {
                    var virtualMachineEntity = VirtualMachineHelper.ConvertToVirtualMachineEntity(
                        virtualMachine,
                        partitionKey,
                        virtualMachine.ResourceGroupName);
                    virtualMachineEntity.VirtualMachineGroup = VirtualMachineGroup.AvailabilitySets.ToString();
                    vmbatchOperation.InsertOrReplace(virtualMachineEntity);
                }
                catch (Exception e)
                {
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                        "GetVmBatchOperation");
                }
            }

            return vmbatchOperation;
        }

        /// <summary>Convert the Availability Set instance to Availability set entity.</summary>
        /// <param name="availabilitySet">The scale set instance.</param>
        /// <returns></returns>
        private static AvailabilitySetsCrawlerResponse ConvertToAvailabilitySetsCrawlerResponse(IAvailabilitySet availabilitySet)
        {
            var availabilitySetEntity = new AvailabilitySetsCrawlerResponse(availabilitySet.ResourceGroupName, availabilitySet.Id.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory))
            {
                EntryInsertionTime = DateTime.UtcNow,
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
