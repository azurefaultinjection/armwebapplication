using AzureChaos.Core.Enums;
using AzureChaos.Core.Models.Configs;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using System;
using System.Configuration;

namespace AzureChaos.Core.Models
{
    /// <summary>Azure configuration model which azure tenant, subscription
    /// and resource group needs to be crawled</summary>
    public class AzureClient
    {
        public readonly IAzure AzureInstance;
        public readonly AzureSettings AzureSettings;

        /// <summary>
        /// Initialize the configuration information and AzureInstance
        /// </summary>
        public AzureClient(TraceWriter log = null)
        {
            try
            {
                AzureSettings = GetAzureSettings(log);
                if (AzureSettings != null)
                {
                    AzureInstance = GetAzure(AzureSettings.Client.ClientId, AzureSettings.Client.ClientSecret,
                        AzureSettings.Client.TenantId, AzureSettings.Client.SubscriptionId);
                }
            }
            catch (Exception)
            {
                // TODO: Logs
            }
        }

        public static IResourceManagementClient GetResourceManagementClientClient(string clientId, string clientSecret, string tenantId, string subscriptionId)
        {
            var azureCredentials = GetAzureCredentials(clientId, clientSecret, tenantId);
            return azureCredentials == null ? null : new ResourceManagementClient(azureCredentials)
            {
                SubscriptionId = subscriptionId
            };
        }

        public static ISubscriptionClient GetSubscriptionClient(string clientId, string clientSecret, string tenantId)
        {
            var azureCredentials = GetAzureCredentials(clientId, clientSecret, tenantId);
            return azureCredentials == null ? null : new SubscriptionClient(azureCredentials);
        }

        /// <summary>Get the Azure object to read the all resources from azure</summary>
        /// <returns>Returns the Azure object.</returns>
        public static IAzure GetAzure(string clientId, string clientSecret, string tenantId, string subscriptionId)
        {
            var azureCredentials = GetAzureCredentials(clientId, clientSecret, tenantId);
            return azureCredentials == null ? null : Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(azureCredentials)
                .WithSubscription(subscriptionId);
        }

        /// <summary>Get azure credentials based on the client id and client secret.</summary>
        /// <returns></returns>
        private static AzureCredentials GetAzureCredentials(string clientId, string clientSecret, string tenantId)
        {
            return SdkContext.AzureCredentialsFactory
                            .FromServicePrincipal(clientId, clientSecret, tenantId, AzureEnvironment.AzureGlobalCloud);
        }

        private static AzureSettings GetAzureSettings(TraceWriter log = null)
        {
            try
            {


                // Zen3 - string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=cmnewschema;AccountKey=Txyvz6P4vUvRBOMrPo8TWE6jtm6JS7PG0+l696iOAua4ZaPXjZhzHtPuFb+Zg8nb5SQLev2flNExlEs7KoimdQ==;EndpointSuffix=core.windows.net";
                // Microsft - string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=azurechaos;AccountKey=4p2a4nzUp3AytDnTm4KY3ERrNfzayowqGWJEZcitqS7fy/QOE/R/a0uT3qjjHVoH6Tb2dG3dC/qpYO4iM0cKHA==;EndpointSuffix=core.windows.net";
                // const string connectionString = "DefaultEndpointsProtocol=https;AccountName=azurechaos;AccountKey=b7yYCgyI9jg5fsRCr08tHzeic0CT5pelmpb2ZMcBaZKWhe8HdycOOs9r3luB2xygOwrbxFBnxLpysjzURKkQLQ==;EndpointSuffix=core.windows.net";
                // const string connectionString = "DefaultEndpointsProtocol=https;AccountName=cmnewschema;AccountKey=Txyvz6P4vUvRBOMrPo8TWE6jtm6JS7PG0+l696iOAua4ZaPXjZhzHtPuFb+Zg8nb5SQLev2flNExlEs7KoimdQ==;EndpointSuffix=core.windows.net";

                // reading connection string from the local.setting.json file.
                // add connection string (i.e. ConfigStorageConnectionString) in the azure portal app settings after the deployment. 
                var connectionString = ConfigurationManager.AppSettings["ConfigStorageConnectionString"];
                if (log != null)
                {
                    log.Info($"connection string {connectionString}");
                }
                // TODO: Add to app settings of the function.
                //  const string connectionString = "UseDevelopmentStorage=true";

                // TODO: Add below code to try catch & log
                var storageAccount = CloudStorageAccount.Parse(connectionString);

                var blobClinet = storageAccount.CreateCloudBlobClient();
                var blobContainer = blobClinet.GetContainerReference("configs");
                var blobReference = blobContainer.GetBlockBlobReference("azuresettings.json");
                var data = blobReference.DownloadText();
                return JsonConvert.DeserializeObject<AzureSettings>(data);
            }
            catch (Exception e)
            {
                if (log != null)
                {
                    log.Error($"connection string {e}");
                }

                throw;
            }
        }

        public bool IsChaosEnabledByGroup(string vmGroup)
        {
            if (!Enum.TryParse(vmGroup, out VirtualMachineGroup virtualMachineGroup))
            {
                return false;
            }

            var chaosEnabled = AzureSettings.Chaos.ChaosEnabled;
            switch (virtualMachineGroup)
            {
                case VirtualMachineGroup.VirtualMachines:
                    return chaosEnabled && AzureSettings.Chaos.VirtualMachineChaos.Enabled;
                case VirtualMachineGroup.AvailabilitySets:
                    return chaosEnabled && AzureSettings.Chaos.AvailabilitySetChaos.Enabled;
                case VirtualMachineGroup.AvailabilityZones:
                    return chaosEnabled && AzureSettings.Chaos.AvailabilityZoneChaos.Enabled;
                case VirtualMachineGroup.VirtualMachineScaleSets:
                    return chaosEnabled && AzureSettings.Chaos.ScaleSetChaos.Enabled;
            }

            return false;
        }
    }
}