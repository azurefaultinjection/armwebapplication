using AzureChaos.Core.Helper;
using AzureChaos.Core.Models;
using AzureChaos.Core.Models.Configs;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Models;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Rest.TransientFaultHandling;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Net.Http.Formatting;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Collections.ObjectModel;

namespace AzureFaultInjection.Controllers
{
    public class FaultInjectionController : ApiController
    {
        private const string StorageConStringFormat = "DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1};EndpointSuffix=core.windows.net";
        private const string CommonName = "AzureFaultInjection";

        // GET: api/Api
        [ActionName("getsubscriptions")]
        public async Task<IEnumerable<SubscriptionInner>> GetSubscriptions(string tenantId, string clientId, string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret) ||
                string.IsNullOrWhiteSpace(tenantId))
            {
                return null;
            }

            var subscriptionClient = AzureClient.GetSubscriptionClient(clientId,
                clientSecret,
                tenantId);

            var subscriptionTask = await subscriptionClient.Subscriptions.ListAsync();
            var subscriptionList = subscriptionTask?.Select(x => x);

            //return response;
            return subscriptionList;
        }

        [ActionName("getresourcegroups")]
        public async Task<IEnumerable<ResourceGroupInner>> GetResourceGroups(string tenantId, string clientId, string clientSecret, string subscription)
        {
            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret) ||
                string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(subscription))
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }

            var subscriptionId = subscription.Split('/').Last();
            var resourceManagementClient = AzureClient.GetResourceManagementClientClient(clientId,
                clientSecret,
                tenantId, subscriptionId);
            var resourceGroupTask = await resourceManagementClient.ResourceGroups.ListAsync();

            var resourceGroupList = resourceGroupTask?.Select(x => x);

            //return response;
            return resourceGroupList;
        }

        [ActionName("blob")]
        public bool Blob([FromBody] FormDataCollection data)
        {
            return false;
        }

        [ActionName("createblob")]
        public ConfigModel CreateBlob(ConfigModel model)
        {
            if (model == null)
            {
                throw new HttpRequestWithStatusException("Empty model", new HttpResponseException(HttpStatusCode.BadRequest));
            }

            if (string.IsNullOrWhiteSpace(model.ClientId) ||
                string.IsNullOrWhiteSpace(model.ClientSecret) ||
                string.IsNullOrWhiteSpace(model.TenantId) ||
                string.IsNullOrWhiteSpace(model.Subscription))
            {
                throw new HttpRequestWithStatusException("ClientId/ClientSecret/TenantId/Subscription is empty", new HttpResponseException(HttpStatusCode.BadRequest));
            }

            try
            {
                var azure = AzureClient.GetAzure(model.ClientId,
                    model.ClientSecret,
                    model.TenantId,
                    model.Subscription);

                var resourceGroupName = GetUniqueHash(model.TenantId + model.ClientId + CommonName);
                model.SelectedRegion = Region.USEast.Name;
                IResourceGroup resourceGroup;
                try
                {
                    resourceGroup = azure.ResourceGroups.GetByName(resourceGroupName);
                }
                catch (Exception e)
                {
                    resourceGroup =
                        ApiHelper.CreateResourceGroup(azure, resourceGroupName, model.SelectedRegion);
                }
                model.SelectedDeploymentRg = resourceGroupName;
                var storageAccountName =
                    GetUniqueHash(model.TenantId + model.ClientId + resourceGroupName + CommonName);
                model.StorageAccountName = storageAccountName;
                var storageAccounts = azure.StorageAccounts.ListByResourceGroup(resourceGroupName);
                IStorageAccount storageAccountInfo;
                if (storageAccounts != null)
                {
                    storageAccountInfo = storageAccounts.FirstOrDefault(x =>
                        x.Name.Equals(storageAccountName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    storageAccountInfo = ApiHelper.CreateStorageAccount(azure, resourceGroup.Name, model.SelectedRegion,
                        storageAccountName);
                }

                if (storageAccountInfo == null)
                {
                    throw new HttpRequestWithStatusException("storage account not created",
                        new HttpResponseException(HttpStatusCode.InternalServerError));
                }

                var storageKeys = storageAccountInfo.GetKeys();
                string storageConnection = string.Format(StorageConStringFormat,
                    model.StorageAccountName, storageKeys[0].Value);

                var storageAccount = CloudStorageAccount.Parse(storageConnection);
                var blockBlob = ApiHelper.CreateBlobContainer(storageAccount);
                var data = blockBlob.DownloadText();
                if (data == null)
                {
                    var configString = ApiHelper.ConvertConfigObjectToString(model);
                    using (var ms = new MemoryStream())
                    {
                        LoadStreamWithJson(ms, configString);
                        blockBlob.UploadFromStream(ms);
                    }
                }
                else
                {
                    var azureSettings = JsonConvert.DeserializeObject<AzureSettings>(data);
                    model = ConvertAzureSettingsConfigModel(azureSettings);
                }

                var functionAppName =
                    GetUniqueHash(model.ClientId + model.TenantId + model.Subscription + CommonName);

                var azureFunctions =
                    azure.AppServices.FunctionApps.ListByResourceGroup(resourceGroupName);
                if (azureFunctions != null && azureFunctions.Count() > 0) return model;
                if (!DeployAzureFunctions(model, functionAppName, storageConnection, resourceGroupName))
                {
                    throw new HttpRequestWithStatusException("Azure Functions are not deployed",
                        new HttpResponseException(HttpStatusCode.InternalServerError));
                }

                return model;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private static ConfigModel ConvertAzureSettingsConfigModel(AzureSettings settings)
        {
            var model = new ConfigModel
            {
                TenantId = settings.Client.TenantId,
                ClientId = settings.Client.ClientId,
                ClientSecret = settings.Client.ClientSecret,
                Subscription = settings.Client.SubscriptionId,
                IsChaosEnabled = settings.Chaos.ChaosEnabled,

                ExcludedResourceGroups = settings.Chaos.ExcludedResourceGroupList,
                IncludedResourceGroups = settings.Chaos.IncludedResourceGroupList,

                AvZoneRegions = settings.Chaos.AvailabilityZoneChaos.Regions,
                IsAvZonesEnabled = settings.Chaos.AvailabilityZoneChaos.Enabled,

                IsAvSetEnabled = settings.Chaos.AvailabilitySetChaos.Enabled,
                IsAvSetsFaultDomainEnabled = settings.Chaos.AvailabilitySetChaos.FaultDomainEnabled,
                IsAvSetsUpdateDomainEnabled = settings.Chaos.AvailabilitySetChaos.UpdateDomainEnabled,

                VmssTerminationPercentage = settings.Chaos.ScaleSetChaos.PercentageTermination,
                IsVmssEnabled = settings.Chaos.ScaleSetChaos.Enabled,

                IsVmEnabled = settings.Chaos.VirtualMachineChaos.Enabled,
                VmTerminationPercentage = settings.Chaos.VirtualMachineChaos.PercentageTermination
            };
            return model;
        }
        private static string GetUniqueHash(string input)
        {
            StringBuilder hash = new StringBuilder();
            MD5CryptoServiceProvider md5Provider = new MD5CryptoServiceProvider();
            byte[] bytes = md5Provider.ComputeHash(new UTF8Encoding().GetBytes(input));

            foreach (var t in bytes)
            {
                hash.Append(t.ToString("x2"));
            }
            return hash.ToString().Substring(0,24);
        }
        private static bool DeployAzureFunctions(ConfigModel model, string functionAppName, string storageConnection, string resourceGroupName)
        {
            try
            {
                var scriptfile = @"D:\callingpowershellscript\deploymentScripts.ps1";
                RunspaceConfiguration runspaceConfiguration = RunspaceConfiguration.Create();

                Runspace runspace = RunspaceFactory.CreateRunspace(runspaceConfiguration);
                runspace.Open();

                RunspaceInvoke scriptInvoker = new RunspaceInvoke(runspace);

                Pipeline pipeline = runspace.CreatePipeline();

                var templateFilePath = @"D:\VSO\ARMTemplate\azuredeploy.json";
                var templateParamPath = @"D:\VSO\ARMTemplate\azuredeploy.parameters.json";
                //Here's how you add a new script with arguments
                Command myCommand = new Command(scriptfile);
                myCommand.Parameters.Add(new CommandParameter("clientId", model.ClientId));
                myCommand.Parameters.Add(new CommandParameter("clientSecret", model.ClientSecret));
                myCommand.Parameters.Add(new CommandParameter("tenantId", model.TenantId));
                myCommand.Parameters.Add(new CommandParameter("subscription", model.Subscription));
                myCommand.Parameters.Add(new CommandParameter("resourceGroupName", resourceGroupName));
                myCommand.Parameters.Add(new CommandParameter("templateFilePath", templateFilePath));
                myCommand.Parameters.Add(new CommandParameter("templateFileParameter", templateParamPath));
                myCommand.Parameters.Add(new CommandParameter("logicAppName", functionAppName));
                myCommand.Parameters.Add(new CommandParameter("functionAppName", functionAppName));
                myCommand.Parameters.Add(new CommandParameter("connectionString", storageConnection));

                pipeline.Commands.Add(myCommand);

                // Execute PowerShell script
                var results = pipeline.Invoke();

                if (results != null)
                {
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        private static Random random = new Random();

        public static string RandomString()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, 9)
                .Select(s => s[random.Next(s.Length)]).ToArray()).ToLower();
        }

        private static void LoadStreamWithJson(Stream ms, string json)
        {
            StreamWriter writer = new StreamWriter(ms);
            writer.Write(json);
            writer.Flush();
            ms.Position = 0;
        }

        // GET: api/Api/5
        public string Get(int id)
        {
            return "value";
        }

        // POST: api/Api
        public void Post([FromBody]string value)
        {
        }

        // PUT: api/Api/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE: api/Api/5
        public void Delete(int id)
        {
        }
    }
}