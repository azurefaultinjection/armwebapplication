using AzureChaos.Core.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AzureChaos.Core.Helper
{
    public class ResourceGroupHelper
    {
        public static List<IResourceGroup> GetResourceGroupsInSubscription()
        {
            var azure = AzureClient.AzureInstance;
            var azureSettings = AzureClient.AzureSettings;
            var blackListedResourceGroupList = azureSettings.Chaos.BlackListedResourceGroupList;
            var inclusiveOnlyResourceGroupList = azureSettings.Chaos.InclusiveOnlyResourceGroupList;
            var resourceGroupList = azure.ResourceGroups.List();
            if (inclusiveOnlyResourceGroupList != null && inclusiveOnlyResourceGroupList.Count > 0)
            {
                return resourceGroupList.Where(x => inclusiveOnlyResourceGroupList.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else if (blackListedResourceGroupList != null && blackListedResourceGroupList.Count > 0)
            {
                return resourceGroupList.Where(x => !blackListedResourceGroupList.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                return resourceGroupList.ToList();
            }
        }
    }
}