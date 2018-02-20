namespace AzureChaos.Core.Constants
{
    /// <summary>
    /// Consists of various storage table names used accross the project
    /// </summary>
    public class StorageTableNames
    {
        public static string ResourceGroupCrawlerTableName = "tblchaosresourcegroup";
        public static string VirtualMachineCrawlerTableName = "tblchaosvirtualmachines";
        public static string AvailabilitySetCrawlerTableName = "tblchaosavailabilityset";
        public static string VirtualMachinesScaleSetCrawlerTableName = "tblchaosscalesets";
        public static string AvailabilityZoneCrawlerTableName = "tblchaosavailabilityzone";
        public static string ActivityLogTableName = "tblchaosactivitylog";
        public static string ScheduledRulesTableName = "tblchaosscheduledrules";
    }
}