using AzureChaos.Core.Enums;
using AzureChaos.Core.Helper;
using AzureChaos.Core.Interfaces;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Linq;

namespace ChaosExecuter.Schedulers
{
    public static class RuleEngineTimer
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        [FunctionName("RuleEngineTimer")]
        public static void Run([TimerTrigger("0 0 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info("C# RuleEngine: trigger function started processing the request.");
            var azureSettings = AzureClient.AzureSettings;
            if (azureSettings?.Chaos == null || !azureSettings.Chaos.ChaosEnabled)
            {
                log.Info("C# RuleEngine: Chaos is not enabled.");
                return;
            }

            var enabledChaos = RuleEngineHelper.GetEnabledChaosSet(StorageProvider, AzureClient);
            if (enabledChaos == null || !enabledChaos.Any())
            {
                log.Info("C# RuleEngine: Chaos is not enabled on any resources.");
                return;
            }

            Random random = new Random();
            var randomIdex = random.Next(0, enabledChaos.Count);
            switch (enabledChaos[randomIdex])
            {
                case VirtualMachineGroup.VirtualMachines:
                    log.Info("C# RuleEngine: Virtual Machine Rule engine got picked");
                    IRuleEngine vm = new VirtualMachineRuleEngine();
                    vm.CreateRule(AzureClient, log);
                    break;
                case VirtualMachineGroup.AvailabilitySets:
                    log.Info("C# RuleEngine: AvailabilitySets Rule engine got picked");
                    IRuleEngine availabilityset = new AvailabilitySetRuleEngine();
                    availabilityset.CreateRule(AzureClient, log);
                    break;
                case VirtualMachineGroup.ScaleSets:
                    log.Info("C# RuleEngine: ScaleSets Rule engine got picked");
                    IRuleEngine vmss = new ScaleSetRuleEngine();
                    vmss.CreateRule(AzureClient, log);
                    break;
                case VirtualMachineGroup.AvailabilityZones:
                    log.Info("C# RuleEngine: AvailabilityZones Rule engine got picked");
                    IRuleEngine availabilityZone = new AvailabilityZoneRuleEngine();
                    availabilityZone.CreateRule(AzureClient, log);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
