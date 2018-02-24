using Newtonsoft.Json;
using System.Collections.Generic;

namespace AzureChaos.Core.Models.Configs
{
    public class ChaosConfig
    {
        [JsonProperty("microsoft.chaos.enabled")]
        public bool ChaosEnabled { get; set; }

        [JsonProperty("microsoft.chaos.leashed")]
        public bool Leashed { get; set; }

        [JsonProperty("microsoft.chaos.meantime")]
        // Donot execute chaos on the resource in mean time more than the minimum time
        public int MeanTime { get; set; }

        [JsonProperty("microsoft.chaos.minimumtime")]
        // Make sure to perform chaos not more than the minimum time on the mean time.
        public int MinimumTime { get; set; }

        [JsonProperty("microsoft.chaos.scheduler.frequency")]
        public int SchedulerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.rollback.fequency")]
        public int RollbackRunFrequency { get; set; }

        [JsonProperty("microsoft.chaos.trigger.frequency")]
        public int TriggerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.crawler.frequency")]
        public int CrawlerFrequency { get; set; }

        [JsonProperty("microsoft.chaos.startTime")]
        public string StartTime { get; set; }

        [JsonProperty("microsoft.chaos.endTime")]
        public string EndTime { get; set; }

        [JsonProperty("microsoft.chaos.notification.global.enabled")]
        public bool NotificationEnabled { get; set; }

        [JsonProperty("microsoft.chaos.notification.sourceEmail")]
        public string SourceEmail { get; set; }

        [JsonProperty("microsoft.chaos.notification.global.receiverEmail")]
        public string ReceiverEmail { get; set; }

        [JsonProperty("microsoft.chaos.blackListedResources")]
        public List<string> BlackListedResources { get; set; }

        [JsonProperty("microsoft.chaos.blackListedResourceGroups")]
        public List<string> BlackListedResourceGroupList { get; set; }

        [JsonProperty("microsoft.chaos.inclusiveOnlyResourceGroups")]
        public List<string> InclusiveOnlyResourceGroupList { get; set; }

        [JsonProperty("microsoft.chaos.AS")]
        public AvailabilitySetChaosConfig AvailabilitySetChaos { get; set; }

        [JsonProperty("microsoft.chaos.SS")]
        public ScaleSetChaosConfig ScaleSetChaos { get; set; }

        [JsonProperty("microsoft.chaos.VM")]
        public VirtualMachineChaosConfig VirtualMachineChaos { get; set; }

        [JsonProperty("microsoft.chaos.AZ")]
        public AvailabilityZoneChaosConfig AvailabilityZoneChaos { get; set; }
    }
}