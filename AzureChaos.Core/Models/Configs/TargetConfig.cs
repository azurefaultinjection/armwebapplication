using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class TargetConfig
    {
        [JsonProperty("microsoft.chaos.client.subscription.id")]
        public string SubscriptionId { get; set; }

        [JsonProperty("microsoft.chaos.client.id")]
        public string ClientId { get; set; }

        [JsonProperty("microsoft.chaos.client.secretKey")]
        public string ClientSecret { get; set; }

        [JsonProperty("microsoft.chaos.client.tenant.id")]
        public string TenantId { get; set; }

        [JsonProperty("microsoft.chaos.client.region")]
        public string Region { get; set; }

        [JsonProperty("microsoft.chaos.client.resourceGroup")]
        public string ResourceGroup { get; set; }

        [JsonProperty("microsoft.chaos.client.storageAccount.name")]
        public string StorageAccountName { get; set; }

        [JsonProperty("microsoft.chaos.client.storageAccount.key")]
        public string StorageAccountKey { get; set; }
    }
}