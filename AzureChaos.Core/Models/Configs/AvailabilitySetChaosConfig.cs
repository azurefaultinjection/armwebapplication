using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class AvailabilitySetChaosConfig
    {
        [JsonProperty("microsoft.chaos.AvSets.enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("microsoft.chaos.AvSets.faultDomain.enabled")]
        public bool FaultDomainEnabled { get; set; }

        [JsonProperty("microsoft.chaos.AvSets.updateDomain.enabled")]
        public bool UpdateDomainEnabled { get; set; }
    }
}