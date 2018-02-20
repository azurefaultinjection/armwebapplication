using AzureChaos.Core.Models.Configs;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using System;

namespace AzureChaos.Core.Models
{
    /// <summary>Azure configuration model which azure tenant, subscription
    /// and resource group needs to be crawled</summary>
    public static class AzureClient
    {
        public static IAzure AzureInstance { get; private set; }

        public static AzureSettings AzureSettings { get; private set; }

        /// <summary>
        /// Initialize the configuration information and AzureInstance
        /// </summary>
        static AzureClient()
        {
            try
            {
                GetAzureSettings();

                if (AzureSettings != null)
                {
                    AzureInstance = GetAzure(AzureSettings.Client.ClientId, AzureSettings.Client.ClientSecret,
                        AzureSettings.Client.TenantId, AzureSettings.Client.SubscriptionId);
                }
            }
            catch (Exception ex)
            {
                // TODO: Logs
            }
        }


        /// <summary>Get the Azure object to read the all resources from azure</summary>
        /// <returns>Returns the Azure object.</returns>
        private static IAzure GetAzure(string clientId, string clientSecret, string tenantId, string subscriptionId)
        {
            var azureCredentials = GetAzureCredentials(clientId, clientSecret, tenantId);
            if (azureCredentials == null)
            {
                return null;
            }

            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(azureCredentials)
                .WithSubscription(subscriptionId);

            return azure;
        }

        /// <summary>Get azure credentials based on the client id and client secret.</summary>
        /// <returns></returns>
        private static AzureCredentials GetAzureCredentials(string clientId, string clientSecret, string tenantId)
        {
            return SdkContext.AzureCredentialsFactory
                            .FromServicePrincipal(clientId, clientSecret, tenantId, AzureEnvironment.AzureGlobalCloud);
        }

        private static void GetAzureSettings()
        {
            // Zen3 - string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=cmnewschema;AccountKey=Txyvz6P4vUvRBOMrPo8TWE6jtm6JS7PG0+l696iOAua4ZaPXjZhzHtPuFb+Zg8nb5SQLev2flNExlEs7KoimdQ==;EndpointSuffix=core.windows.net";
            // Microsft - string ConnectionString = "DefaultEndpointsProtocol=https;AccountName=azurechaos;AccountKey=4p2a4nzUp3AytDnTm4KY3ERrNfzayowqGWJEZcitqS7fy/QOE/R/a0uT3qjjHVoH6Tb2dG3dC/qpYO4iM0cKHA==;EndpointSuffix=core.windows.net";
            const string connectionString = "DefaultEndpointsProtocol=https;AccountName=azurechaos;AccountKey=b7yYCgyI9jg5fsRCr08tHzeic0CT5pelmpb2ZMcBaZKWhe8HdycOOs9r3luB2xygOwrbxFBnxLpysjzURKkQLQ==;EndpointSuffix=core.windows.net";

            // TODO: Add to app settings of the function. 
          //  const string connectionString = "UseDevelopmentStorage=true";

            // TODO: Add below code to try catch & log
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            var blobClinet = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClinet.GetContainerReference("configs");
            var blobReference = blobContainer.GetBlockBlobReference("azuresettings.json");
            //var tas = Task.Run(() => blobReference.DownloadTextAsync());
            var data = blobReference.DownloadText();
            AzureSettings = JsonConvert.DeserializeObject<AzureSettings>(data);
        }

    }
}