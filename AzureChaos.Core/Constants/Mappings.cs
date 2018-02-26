using AzureChaos.Core.Enums;
using AzureChaos.Core.Models.Configs;
using System.Collections.Generic;

namespace AzureChaos.Core.Constants
{
    public class Mappings
    {
        public static IDictionary<VirtualMachineGroup, bool> GetEnabledChaos(AzureSettings azureSettings)
        {
            return new Dictionary<VirtualMachineGroup, bool>()
            {
                { VirtualMachineGroup.AvailabilitySets, azureSettings.Chaos.AvailabilitySetChaos.Enabled},
                { VirtualMachineGroup.VirtualMachines, azureSettings.Chaos.VirtualMachineChaos.Enabled},
                { VirtualMachineGroup.AvailabilityZones, azureSettings.Chaos.AvailabilityZoneChaos.Enabled},
                { VirtualMachineGroup.VirtualMachineScaleSets, azureSettings.Chaos.ScaleSetChaos.Enabled}
            };
        }

        public static Dictionary<string, string> FunctionNameMap = new Dictionary<string, string>()
        {
            { VirtualMachineGroup.VirtualMachines.ToString(), "virtualmachinesexecuter" },
            { VirtualMachineGroup.VirtualMachineScaleSets.ToString(), "virtualmachinescalesetexecuter" },
            { VirtualMachineGroup.AvailabilitySets.ToString(), "virtualmachinesexecuter" },
            { VirtualMachineGroup.AvailabilityZones.ToString(), "virtualmachinesexecuter" },
        };

        ///Microsoft subscription blob endpoint for configs:  https://chaostest.blob.core.windows.net/config/azuresettings.json
        ///Zen3 subscription blob endpoint for configs: ==>  https://cmonkeylogs.blob.core.windows.net/configs/azuresettings.json
        /// Microsoft demo config file ==> https://stachaosteststorage.blob.core.windows.net/configs/azuresettings.json

        public const string ConfigEndpoint = "https://cmnewschema.blob.core.windows.net/configs/azuresettings.json";
    }
}