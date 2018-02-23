using Newtonsoft.Json;

namespace AzureChaos.Core.Models.Configs
{
    public class AzureSettings
    {
        [JsonProperty("ClientConfig")]
        public ClientConfig Client { get; set; }

        [JsonProperty("ChaosConfig")]
        public ChaosConfig Chaos { get; set; }
    }
}