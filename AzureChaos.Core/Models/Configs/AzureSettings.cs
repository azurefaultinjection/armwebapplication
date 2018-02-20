using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class AzureSettings
    {
        [JsonProperty("ClientConfig")]
        public ClientConfig Client { get; set; }

        [JsonProperty("ChaosConfig")]
        public ChaosConfig Chaos { get; set; }

        [JsonProperty("microsoft.chaos.client.table.resourceGroupCrawler")]
        public string ResourceGroupCrawlerTableName { get; set; }

        [JsonProperty("microsoft.chaos.client.table.virtualMachineCrawler")]
        public string VirtualMachineCrawlerTableName { get; set; }

        [JsonProperty("microsoft.chaos.client.table.availabilitySetCrawler")]
        public string AvailabilitySetCrawlerTableName { get; set; }

        [JsonProperty("microsoft.chaos.client.table.scaleSetCrawler")]
        public string ScaleSetCrawlerTableName { get; set; }

        [JsonProperty("microsoft.chaos.client.table.availabilityZoneCrawler")]
        public string AvailabilityZoneCrawlerTableName { get; set; }

        [JsonProperty("microsoft.chaos.client.table.activityLog")]
        public string ActivityLogTable { get; set; }

        [JsonProperty("microsoft.chaos.client.table.scheduledRules")]
        public string ScheduledRulesTable { get; set; }
    }
}