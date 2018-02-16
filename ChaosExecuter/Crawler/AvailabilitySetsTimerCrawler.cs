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
    public static class AvailabilitySetsTimerCrawler
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        // TODO: need to read the crawler timer from the configuration.
        [FunctionName("timercrawlerforavailabilitysets")]
        public static async Task Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerforavailabilitysets executed at: {DateTime.UtcNow}");

            var azureSettings = AzureClient.AzureSettings;
            var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription(AzureClient.AzureInstance, azureSettings);
            if (resourceGroupList == null)
            {
                log.Info($"timercrawlerforavailabilitysets: no resource groups to crawler");
                return;
            }

           await GetAndInsertAvailiabilitySetsForResourceGroupsAsync(resourceGroupList, log, azureSettings);
        }

        /// <summary>1. Iterate the resource groups to get the availability sets for individual resource group.
        /// 2. Convert the List of availability sets into availability set entity and add them into the table batch operation.
        /// 3. Get the list of virtual machines, convert into entity and add them into the table batach operation
        /// 3. Everything will happen parallel using TPL Parallel.Foreach</summary>
        /// <param name="resourceGroups">List of resource groups for the particular subscription.</param>
        /// <param name="log">Trace writer instance</param>
        /// <param name="azureSettings">Azure settings configuration to get the table name of scale set and virtual machine.</param>
        private static async Task GetAndInsertAvailiabilitySetsForResourceGroupsAsync(IEnumerable<IResourceGroup> resourceGroups,
            TraceWriter log, AzureSettings azureSettings)
        {
            try
            {
                var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);
                var vmTable = StorageProvider.CreateOrGetTableAsync(storageAccount,
                        azureSettings.VirtualMachineCrawlerTableName);
                var availabilitySetTable = StorageProvider.CreateOrGetTableAsync(storageAccount,
                        azureSettings.AvailabilitySetCrawlerTableName);

                await Task.WhenAll(vmTable, availabilitySetTable);

                // using parallel here to run all the resource groups parallelly, parallel is 10times faster than normal foreach.
                Parallel.ForEach(resourceGroups, async resourceGroup =>
                {
                    try
                    {
                        log.Info("Resource Group: " + resourceGroup.Name);
                        var availabilitySets =
                            await AzureClient.AzureInstance.AvailabilitySets.ListByResourceGroupAsync(
                                resourceGroup.Name);
                        var tasks = InsertOrReplaceAvailabilitySetEntitiesIntoTable(availabilitySets, vmTable.Result,
                            availabilitySetTable.Result, log);
                        if (tasks != null && tasks.Any())
                        {
                            await Task.WhenAll(tasks);
                        }
                    }
                    catch (Exception e)
                    {
                        //  catch the error, to continue adding other entities to table
                        log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", e,
                            "GetScaleSetsForResourceGroups: for the resource group " + resourceGroup.Name);
                    }
                });
            }
            catch (Exception ex)
            {
                log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", ex,
                    "GetScaleSetsForResourceGroups");
            }
        }

        /// <summary>1. Get all the virtual machines from the each availability set and add them into batch operation
        /// 3. Combine all the tasks and return the list of tasks.</summary>
        /// <param name="availabilitySets">List of availability sets for the resource group</param>
        /// <param name="vmTable">Get the virtual machine table instance</param>
        /// <param name="availabilitySetSetTable">Get the scale set table instance</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns></returns>
        private static List<Task> InsertOrReplaceAvailabilitySetEntitiesIntoTable(
            IPagedCollection<IAvailabilitySet> availabilitySets,
            CloudTable vmTable,
            CloudTable availabilitySetSetTable,
            TraceWriter log)
        {
            if (availabilitySets == null || vmTable == null || availabilitySetSetTable == null)
            {
                return null;
            }

            var tasks = new List<Task>();
            var availabilitySetbatchOperation = new TableBatchOperation();
            Parallel.ForEach(availabilitySets, async availabilitySet =>
            {
                try
                {
                    log.Info("availabilitySet name: " + availabilitySet.Name + "resource group name: " + availabilitySet.ResourceGroupName);
                    availabilitySetbatchOperation.InsertOrReplace(ConvertToAvailabilitySetsCrawlerResponse(availabilitySet));
                    var pageCollection = await AzureClient.AzureInstance.VirtualMachines.ListByResourceGroupAsync(availabilitySet.ResourceGroupName);
                    if (pageCollection != null)
                    {
                        var virtualMachinesList = pageCollection.Where(x =>
                            availabilitySet.Id.Equals(x.AvailabilitySetId, StringComparison.OrdinalIgnoreCase));

                        // get the scale set instances
                        var vmBatchOperation = InsertOrReplaceVirtualMachinesIntoTable(virtualMachinesList,
                            availabilitySet.ResourceGroupName,
                            availabilitySet.Id, log);
                        if (vmBatchOperation != null && vmBatchOperation.Count > 0)
                        {
                            tasks.Add(vmTable.ExecuteBatchAsync(vmBatchOperation));
                        }
                    }
                }
                catch (Exception e)
                {
                    //  catch the error, to continue adding other entities to table
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ",
                        e,
                        "InsertOrReplaceAvailabilitySetEntitiesIntoTable for the availability set: " +
                        availabilitySet.Name);
                }
            });

            if (availabilitySetbatchOperation.Any())
            {
                tasks.Add(availabilitySetSetTable.ExecuteBatchAsync(availabilitySetbatchOperation));
            }

            return tasks;
        }

        private static TableBatchOperation InsertOrReplaceVirtualMachinesIntoTable(IEnumerable<IVirtualMachine> virtualMachines,
            string resourceGroupName,
            string availabilitySetId, TraceWriter log)
        {
            if (virtualMachines == null)
            {
                return null;
            }

            var vmbatchOperation = new TableBatchOperation();
            var partitionKey = availabilitySetId.Replace(Delimeters.ForwardSlash, Delimeters.Exclamatory);
            Parallel.ForEach(virtualMachines, virtualMachine =>
            {
                log.Info("virtual machine name: " + virtualMachine.Name + "availabilitySet id: " + availabilitySetId);
                var virtualMachineEntity = VirtualMachineHelper.ConvertToVirtualMachineEntity(
                    virtualMachine,
                    partitionKey,
                    resourceGroupName);
                virtualMachineEntity.VirtualMachineGroup = VirtualMachineGroup.AvailabilitySets.ToString();
                vmbatchOperation.InsertOrReplace(virtualMachineEntity);
            });

            return vmbatchOperation;
        }

        /// <summary>Convert the Availability Set instance to scale set entity.</summary>
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