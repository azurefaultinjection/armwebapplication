﻿using Newtonsoft.Json;
using System.Collections.Generic;

namespace AzureChaos.Core.Models.Configs
{
    public class AvailabilityZoneChaosConfig
    {
        [JsonProperty("microsoft.chaos.AZ.enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("microsoft.chaos.AZ.regions")]
        public List<string> Regions { get; set; } = new List<string>();
    }
}