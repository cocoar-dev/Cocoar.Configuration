namespace ConfigurationShowcase.Models;

public class DiagnosticsConfig
{
    public bool EnableRequestTracing { get; set; }
    public bool EnablePerformanceCounters { get; set; }
    public int TraceRetentionMinutes { get; set; } = 60;
    public string TraceEndpoint { get; set; } = "/diagnostics/traces";
}
