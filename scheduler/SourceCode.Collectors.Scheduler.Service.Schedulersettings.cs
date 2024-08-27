namespace Microsoft.CloudMine.SourceCode.Collectors.Scheduler.Service
{
    public record SchedulerSettings
    {
        public required string ConfigurationQuery { get; set; }
        public required int MaxTimeout { get; set; }
        public required int MaxReruns { get; set; }
    }
}
