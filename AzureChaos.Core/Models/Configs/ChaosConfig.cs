using Newtonsoft.Json;
using System.Collections.Generic;

namespace AzureChaos.Core.Models.Configs
{
    public class ChaosConfig
    {
        [JsonProperty("microsoft.chaos.enabled")]
        public bool ChaosEnabled { get; set; }

        [JsonProperty("microsoft.chaos.meantime")]
        // Donot execute chaos on the resource in mean time more than the minimum time
        public int MeanTime { get; set; }

        [JsonProperty("microsoft.chaos.scheduler.frequency")]
        public int SchedulerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.rollback.frequency")]
        public int RollbackRunFrequency { get; set; }

        [JsonProperty("microsoft.chaos.trigger.frequency")]
        public int TriggerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.crawler.frequency")]
        public int CrawlerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.notification.global.enabled")]
        public bool NotificationEnabled { get; set; }

        [JsonProperty("microsoft.chaos.notification.sourceEmail")]
        public string SourceEmail { get; set; }

        [JsonProperty("microsoft.chaos.notification.global.receiverEmail")]
        public string ReceiverEmail { get; set; }

        [JsonProperty("microsoft.chaos.excludedResourceGroups")]
        public List<string> ExcludedResourceGroupList { get; set; }

        [JsonProperty("microsoft.chaos.includedResourceGroups")]
        public List<string> IncludedResourceGroupList { get; set; }

        [JsonProperty("microsoft.chaos.AvSets")]
        public AvailabilitySetChaosConfig AvailabilitySetChaos { get; set; }

        [JsonProperty("microsoft.chaos.VmSS")]
        public ScaleSetChaosConfig ScaleSetChaos { get; set; }

        [JsonProperty("microsoft.chaos.VM")]
        public VirtualMachineChaosConfig VirtualMachineChaos { get; set; }

        [JsonProperty("microsoft.chaos.AvZones")]
        public AvailabilityZoneChaosConfig AvailabilityZoneChaos { get; set; }
    }
}