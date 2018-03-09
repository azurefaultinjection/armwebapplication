using System.Collections.Generic;
using Newtonsoft.Json;

namespace AzureChaos.Core.Models
{
    public class ConfigModel
    {
        [JsonProperty("tenantId")] public string TenantId { get; set; }

        [JsonProperty("clientId")] public string ClientId { get; set; }

        [JsonProperty("clientSecret")] public string ClientSecret { get; set; }

        [JsonProperty("selectedSubscription")] public string SelectedSubscription { get; set; }

        [JsonProperty("selectedDeploymentRg")] public string SelectedDeploymentRg { get; set; }

        [JsonProperty("selectedRegion")] public string SelectedRegion { get; set; }

        [JsonProperty("storageAccountName")] public string StorageAccountName { get; set; }

        public string StorageConnectionString { get; set; }

        [JsonProperty("selectedExcludedRg")] public List<string> ExcludedResourceGroups { get; set; }

        [JsonProperty("selectedIncludedRg")] public List<string> IncludedResourceGroups { get; set; }

        [JsonProperty("isChaosEnabled")] public bool IsChaosEnabled { get; set; }

        [JsonProperty("schedulerFrequency")] public int SchedulerFrequency { get; set; }

        [JsonProperty("rollbackFrequency")] public int RollbackFrequency { get; set; }

        [JsonProperty("triggerFrequency")] public int TriggerFrequency { get; set; }

        [JsonProperty("crawlerFrequency")] public int CrawlerFrequency { get; set; }

        [JsonProperty("meanTime")] public int MeanTime { get; set; }

        [JsonProperty("isAvZonesEnabled")] public bool IsAvZonesEnabled { get; set; }

        [JsonProperty("AvZoneRegions")] public List<string> AvZoneRegions { get; set; }

        [JsonProperty("isVmEnabled")] public bool IsVmEnabled { get; set; }

        [JsonProperty("vmTerminationPercentage")]
        public int VmTerminationPercentage { get; set; }

        [JsonProperty("isVmssEnabled")] public bool IsVmssEnabled { get; set; }

        [JsonProperty("vmssTerminationPercentage")]
        public int VmssTerminationPercentage { get; set; }

        [JsonProperty("isAvSetsEnabled")] public bool IsAvSetsEnabled { get; set; }

        [JsonProperty("isAvSetsFaultDomainEnabled")]
        public bool IsAvSetsFaultDomainEnabled { get; set; }

        [JsonProperty("isAvSetsUpdateDomainEnabled")]
        public bool IsAvSetsUpdateDomainEnabled { get; set; }
    }
}