using AzureChaos.Core.Constants;
using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using AzureChaos.Core.Models;
using AzureChaos.Core.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ChaosExecuter.Executer
{
    public static class VirtualMachineScaleSetVmExecuter
    {
        private const string FunctionName = "virtualmachinescalesetexecuter";

        [FunctionName("virtualmachinescalesetexecuter")]
        public static async Task<bool> Run([OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<string>();
            if (!ValidateInput(input, log, out var inputObject))
            {
                return false;
            }

            var azureClient = new AzureClient();
            var scheduleRule = new ScheduledRules(inputObject.PartitionKey, inputObject.RowKey)
            {
                ExecutionStatus = Status.Started.ToString()
            };
            try
            {
                var scaleSetVm = await GetVirtualMachineScaleSetVm(azureClient.AzureInstance, inputObject, log);
                if (scaleSetVm == null)
                {
                    log.Info($"VM Scaleset Chaos : No resource found for the  scale set id: " + inputObject.VirtualMachineScaleSetId);
                    return false;
                }

                log.Info($"VM ScaleSet Chaos received the action: " + inputObject.Action +
                         " for the virtual machine: " + inputObject.ResourceId);

                SetInitialEventActivity(scaleSetVm, scheduleRule);

                // if its not valid chaos then update the event table with  warning message and return the bad request response
                bool isValidChaos = IsValidChaos(inputObject.Action, scaleSetVm.PowerState);
                if (!isValidChaos)
                {
                    log.Info($"VM ScaleSet- Invalid action: " + inputObject.Action);
                    scheduleRule.ExecutionStatus = Status.Failed.ToString();
                    scheduleRule.Warning = Warnings.ActionAndStateAreSame;
                    StorageAccountProvider.InsertOrMerge(scheduleRule, StorageTableNames.ScheduledRulesTableName);
                    return false;
                }

                scheduleRule.ExecutionStatus = Status.Started.ToString();
                StorageAccountProvider.InsertOrMerge(scheduleRule, StorageTableNames.ScheduledRulesTableName);
                await PerformChaos(inputObject.Action, scaleSetVm, scheduleRule);
                scaleSetVm = await scaleSetVm.RefreshAsync();
                if (scaleSetVm != null)
                {
                    scheduleRule.EventCompletedTime = DateTime.UtcNow;
                    scheduleRule.FinalState = scaleSetVm.PowerState.Value;
                    scheduleRule.ExecutionStatus = Status.Completed.ToString();
                }

                StorageAccountProvider.InsertOrMerge(scheduleRule, StorageTableNames.ScheduledRulesTableName);
                log.Info($"VM ScaleSet Chaos Completed");
                return true;
            }
            catch (Exception ex)
            {
                scheduleRule.Error = ex.Message;
                scheduleRule.ExecutionStatus = Status.Failed.ToString();
                StorageAccountProvider.InsertOrMerge(scheduleRule, StorageTableNames.ScheduledRulesTableName);

                // dont throw the error here just handle the error and return the false
                log.Error($"VM ScaleSet Chaos trigger function threw the exception ", ex, FunctionName);
                log.Info($"VM ScaleSet Chaos Completed with error");
            }

            return false;
        }

        /// <summary>Validate the request input on this functions, and log the invalid.</summary>
        /// <param name="input"></param>
        /// <param name="log"></param>
        /// <param name="inputObject"></param>
        /// <returns></returns>
        private static bool ValidateInput(string input, TraceWriter log, out InputObject inputObject)
        {
            try
            {
                inputObject = JsonConvert.DeserializeObject<InputObject>(input);
                if (inputObject == null)
                {
                    log.Error("input data is empty");
                    return false;
                }
                if (!Enum.TryParse(inputObject.Action.ToString(), out ActionType _))
                {
                    log.Error("Virtual Machine action is not valid action");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(inputObject.ResourceId))
                {
                    log.Error("Virtual Machine Resource name is not valid name");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(inputObject.ResourceGroup))
                {
                    log.Error("Virtual Machine Resource Group is not valid resource group");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(inputObject.VirtualMachineScaleSetId))
                {
                    log.Error("VMScaleset Id is not valid ");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log.Error("Threw exception on the validate input method", ex, FunctionName + ": ValidateInput");
                inputObject = null;
                return false;
            }

            return true;
        }

        /// <summary>Check the given action is valid chaos to perform on the scale set vm</summary>
        /// <param name="currentAction">Current request action</param>
        /// <param name="state">Current scale set Vm state.</param>
        /// <returns></returns>
        private static bool IsValidChaos(ActionType currentAction, PowerState state)
        {
            switch (currentAction)
            {
                case ActionType.Start:
                    return state != PowerState.Running && state != PowerState.Starting;

                case ActionType.Stop:
                case ActionType.PowerOff:
                case ActionType.Restart:
                    return state != PowerState.Stopping && state != PowerState.Stopped;

                default:
                    return false;
            }
        }

        /// <summary>Perform the Chaos Operation</summary>
        /// <param name="actionType">Action type</param>
        /// <param name="scaleSetVm">Virtual Machine instance</param>
        /// <param name="scheduleRule">Event activity entity</param>
        /// <returns></returns>
        private static async Task PerformChaos(ActionType actionType, IVirtualMachineScaleSetVM scaleSetVm, ScheduledRules scheduleRule)
        {
            switch (actionType)
            {
                case ActionType.Start:
                    await scaleSetVm.StartAsync();
                    break;

                case ActionType.PowerOff:
                case ActionType.Stop:
                    await scaleSetVm.PowerOffAsync();
                    break;

                case ActionType.Restart:
                    await scaleSetVm.RestartAsync();
                    break;
            }

            scheduleRule.ExecutionStatus = Status.Executing.ToString();
        }

        /// <summary>Set the initial property of the activity entity</summary>
        /// <param name="scaleSetVm">The vm</param>
        /// <param name="scheduleRule">Event activity entity.</param>
        private static void SetInitialEventActivity(IVirtualMachineScaleSetVM scaleSetVm, ScheduledRules scheduleRule)
        {
            scheduleRule.InitialState = scaleSetVm.PowerState.Value;
            scheduleRule.ExecutionStartTime = DateTime.UtcNow;
        }

        /// <summary>Get the virtual machine.</summary>
        /// <param name="azure">The azure client instance</param>
        /// <param name="inputObject">The input request.</param>
        /// <param name="log">The trace writer instance</param>
        /// <returns>Returns the virtual machine.</returns>
        private static async Task<IVirtualMachineScaleSetVM> GetVirtualMachineScaleSetVm(IAzure azure, InputObject inputObject, TraceWriter log)
        {
            var vmScaleSet = await azure.VirtualMachineScaleSets.GetByIdAsync(inputObject.VirtualMachineScaleSetId);
            if (vmScaleSet == null)
            {
                log.Info("VM Scaleset Chaos: scale set is returning null for the Id: " + inputObject.VirtualMachineScaleSetId);
                return null;
            }

            var scaleSetVms = await vmScaleSet.VirtualMachines.ListAsync();
            if (scaleSetVms != null && scaleSetVms.Any())
            {
                return scaleSetVms.FirstOrDefault(x =>
                    x.Name.Equals(inputObject.ResourceId, StringComparison.OrdinalIgnoreCase));
            }

            log.Info("VM Scaleset Chaos: scale set vm's are empty");
            return null;
        }
    }
}