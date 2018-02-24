namespace AzureChaos.Core.Models
{
    public class Activities
    {
        public string ResourceId { get; set; }
        public string ChaosOperation { get; set; }
        public string IsRolbacked { get; set; }
        public  string ChaosCompletedTime { get; set; }
        public string ChaosStartedTime { get; set; }
        public string FinalState { get; set; }
        public string InitialState { get; set; }
        public string Warning { get; set; }
        public string Error { get; set; }
        public string Status { get; set; }
    }
}
