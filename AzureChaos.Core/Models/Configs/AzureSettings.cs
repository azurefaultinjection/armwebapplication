using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class AzureSettings
    {
        [JsonProperty("ClientConfig")]
        public TargetConfig Client { get; set; }

        [JsonProperty("ChaosConfig")]
        public ChaosConfig Chaos { get; set; }
    }
}