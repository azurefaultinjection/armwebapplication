using AzureChaos.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using AzureChaos.Core.Helper;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;

namespace AzureFaultInjection.Controllers
{
    public class FaultInjectionController : ApiController
    {
        private const string StorageConStringFormat = "DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1};EndpointSuffix=core.windows.net";
        private const string ResourceGroup = "AzureFaultInjection";
        private const string StorageAccountName = "faultinjection";

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
                return null;
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

        [ActionName("createblob")]
        public bool CreateBlob(ConfigModel model)
        {
            var azure = AzureClient.GetAzure(model.ClientId,
                model.ClientSecret,
                model.TenantId,
                model.SelectedSubscription);
            model.SelectedRegion = Region.USEast.Name;
            model.StorageAccountName = StorageAccountName + RandomString();
            var resourceGroup =
                ApiHelper.CreateResourceGroup(azure, ResourceGroup, model.SelectedRegion);

            var storage =
                ApiHelper.CreateStorageAccount(azure, resourceGroup.Name, model.SelectedRegion, model.StorageAccountName);

            var storageKeys = storage.GetKeys();
            model.StorageConnectionString = string.Format(StorageConStringFormat,
                model.StorageAccountName, storageKeys[0].Value);
            var storageAccount = CloudStorageAccount.Parse(model.StorageConnectionString);
            var configString = ApiHelper.ConvertConfigObjectToString(model);
            var blockBlob = ApiHelper.CreateBlobContainer(storageAccount);

            using (var ms = new MemoryStream())
            {
                LoadStreamWithJson(ms, configString);
                blockBlob.UploadFromStream(ms);
            }

            return true;
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