using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class ScaleSetChaosConfig
    {
        [JsonProperty("microsoft.chaos.VmSS.enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("microsoft.chaos.VmSS.percentageTermination")]
        public decimal PercentageTermination { get; set; }
    }
}