using System.ComponentModel.DataAnnotations;

namespace AzureChaos.Core.Entity
{
    public class AvailabilitySetsCrawlerResponse : CrawlerResponse
    {
        public AvailabilitySetsCrawlerResponse()
        { }

        public AvailabilitySetsCrawlerResponse(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }

        [Required] public string Key { get; set; }

        /// <summary>Triggered Event </summary>
        public bool HasVirtualMachines { get; set; }

        [Required] public int FaultDomainCount { get; set; }

        [Required] public int UpdateDomainCount { get; set; }
    }
}