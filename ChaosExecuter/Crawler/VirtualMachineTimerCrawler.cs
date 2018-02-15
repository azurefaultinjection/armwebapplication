using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    /// <summary>Crawl the stand alone virtual machines (i.e. exclude the availiability set and load balancer virtual machines.) 
    /// from the resource groups which are specified in the configuration file. </summary>
    public static class VirtualMachineTimerCrawler
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        // TODO: need to read the crawler timer from the configuration.
        [FunctionName("timercrawlerforvirtualmachines")]
        public static async void Run([TimerTrigger("0 */2 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerforvirtualmachines executed at: {DateTime.UtcNow}");
            try
            {
                var azureSettings = AzureClient.AzureSettings;
                var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription(AzureClient.AzureInstance, azureSettings);
                if (resourceGroupList == null)
                {
                    log.Info($"timercrawlerforvirtualmachines: no resource groups to crawl");
                    return;
                }

                await GetVirtualMachinesForResourceGroups(resourceGroupList, log, azureSettings);
            }
            catch (Exception ex)
            {
                log.Error($"timercrawlerforvirtualmachines threw the exception ", ex, "timercrawlerforvirtualmachines");
            }

            log.Info($"timercrawlerforvirtualmachines end  at: {DateTime.UtcNow}");
        }

        /// <summary>1. Iterate the resource groups to get the virtual machines for individual resource group.
        /// 2. Convert the List of virtual machines into entities and add them into tasklist, repeat this process for the VM's under the resource group.
        /// 3. Execute all the task parallely</summary>
        /// <param name="resourceGroups">List of resource groups for the particular subscription.</param>
        /// <param name="log">Trace writer instance</param>
        /// <param name="azureSettings">Azure settings configuration to get the table name of the virtual machine crawler.</param>
        private static async Task GetVirtualMachinesForResourceGroups(IEnumerable<IResourceGroup> resourceGroups, TraceWriter log, AzureSettings azureSettings)
        {
            try
            {
                var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);
                var table = await StorageProvider.CreateOrGetTableAsync(storageAccount, azureSettings.VirtualMachineCrawlerTableName);
                var taskList = new List<Task>();
                foreach (var resourceGroup in resourceGroups)
                {
                    log.Info($"timercrawlerforvirtualmachines: crawling virtual machines for  rg:" +
                             resourceGroup.Name);
                    var virtualMachines = await GetVirtualMachinesByResourceGroup(resourceGroup, log);
                    if (virtualMachines == null)
                    {
                        continue;
                    }

                    var tasks = InsertOrReplaceEntitiesIntoTable(virtualMachines.ToList(), table, log);
                    if (tasks != null)
                    {
                        taskList.AddRange(tasks);
                    }
                }

                if (taskList.Count > 0)
                {
                    await Task.WhenAll(taskList);
                }
            }
            catch (Exception e)
            {
                log.Error($"timercrawlerforvirtualmachines:threw exception", e, "GetVirtualMachinesForResourceGroups");
            }
        }

        /// <summary>1. Get the List of virtual machines for the resource group.
        /// 2. Get all the virtual machines from the load balancers.</summary>
        /// <param name="resourceGroup">From which resource group needs to get the virtual machines.</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns>List of virtual machines which excludes the load balancer virtual machines and availability set virtual machines.</returns>
        private static async Task<IEnumerable<IVirtualMachine>> GetVirtualMachinesByResourceGroup(IResourceGroup resourceGroup, TraceWriter log)
        {
            var loadBalancersVms = Task.Run(() => GetVirtualMachinesFromLoadBalancers(resourceGroup.Name, log));
            var pagedCollection = Task.Run(() => AzureClient.AzureInstance.VirtualMachines.ListByResourceGroupAsync(resourceGroup.Name));
            var tasks = new List<Task>
            {
                loadBalancersVms,
                pagedCollection
            };

            await Task.WhenAll(tasks);
            if (pagedCollection.Result == null || !pagedCollection.Result.Any())
            {
                log.Info($"timercrawlerforvirtualmachines: no virtual machines for the resource group: " + resourceGroup.Name);
                return null;
            }

            var loadBalancerIds = loadBalancersVms.Result;
            var virtuallMachines = pagedCollection.Result;
            return virtuallMachines?.Select(x => x).Where(x => string.IsNullOrWhiteSpace(x.AvailabilitySetId) &&
                                                                                !loadBalancerIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>1. Convert the list of virtual machines into Virtual machine crawler entities
        /// 2. Add the entity into the table using the batch operation task</summary>
        /// <param name="virtualMachines">List of virtual machines, which needs to push into table</param>
        /// <param name="table">Name of the table, where the virtual machines needs to push.</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns>Returns the list of the insert batch operation as task.</returns>
        private static List<Task> InsertOrReplaceEntitiesIntoTable(List<IVirtualMachine> virtualMachines, CloudTable table, TraceWriter log)
        {
            if (virtualMachines.Count == 0)
            {
                return null;
            }

            var tasks = new List<Task>();
            // Grouping based on the resource group name, to keep the resource group name as the partition key for the stand alone virtual machines.
            var groupList = virtualMachines.GroupBy(x => x.ResourceGroupName).Select(x => x).ToList();
            foreach (var groupItem in groupList)
            {
                try
                {
                    var batchOperation = new TableBatchOperation();
                    foreach (var virtualMachine in groupItem)
                    {
                        var partitionKey = virtualMachine.ResourceGroupName;
                        batchOperation.InsertOrReplace(VirtualMachineHelper.ConvertToVirtualMachineEntity(virtualMachine, partitionKey));
                    }

                    if (batchOperation.Count <= 0) continue;
                    table.ExecuteBatchAsync(batchOperation);
                    tasks.Add(table.ExecuteBatchAsync(batchOperation));
                }
                catch (Exception e)
                {
                    log.Error($"timercrawlerforvirtualmachines threw the exception ", e, "InsertEntitiesIntoTable");
                }
            }

            return tasks;
        }

        /// <summary>Get the list of the load balancer virtual machines by resource group.</summary>
        /// <param name="resourceGroup">The resource group name.</param>
        /// <param name="log">Trace writer instance</param>
        /// <returns>Returns the list of vm ids which are in the load balancers.</returns>
        private static async Task<List<string>> GetVirtualMachinesFromLoadBalancers(string resourceGroup, TraceWriter log)
        {
            log.Info($"timercrawlerforvirtualmachines getting the load balancer virtual machines");
            var vmIds = new List<string>();
            var pagedCollection = await AzureClient.AzureInstance.LoadBalancers.ListByResourceGroupAsync(resourceGroup);
            if (pagedCollection == null)
            {
                return vmIds;
            }

            var loadBalancers = pagedCollection.Select(x => x);
            var balancers = loadBalancers.ToList();
            if (!balancers.Any())
            {
                return vmIds;
            }

            vmIds.AddRange(balancers.SelectMany(x => x.Backends).SelectMany(x => x.Value.GetVirtualMachineIds()));
            return vmIds;
        }
    }
}