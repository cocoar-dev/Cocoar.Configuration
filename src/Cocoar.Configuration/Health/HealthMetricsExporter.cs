namespace Cocoar.Configuration.Health;

public sealed class HealthMetricsExporter : IDisposable
{
    private readonly IConfigurationHealthService _health;
    private readonly ISimpleHealthMetricsSink _sink;
    private readonly IDisposable _subscription;
    private long _lastSnapshotId;

    public HealthMetricsExporter(IConfigurationHealthService healthService, ISimpleHealthMetricsSink sink)
    {
        _health = healthService;
        _sink = sink;
        _subscription = _health.SnapshotStream.Subscribe(OnSnapshot);
    }

    private void OnSnapshot(ConfigHealthSnapshot snapshot)
    {
        
        if (snapshot.Id == _lastSnapshotId)
        {
            return;
        }

        _lastSnapshotId = snapshot.Id;
        _sink.Report(new(
            snapshot.Id,
            snapshot.TimestampUtc,
            snapshot.ConfigVersion,
            snapshot.OverallStatus,
            snapshot.Summary.RequiredFailed,
            snapshot.Summary.OptionalFailed,
            snapshot.Summary.Skipped));
    }

    public void Dispose() => _subscription.Dispose();
}

public readonly record struct HealthMetrics(
    long SnapshotId,
    DateTime TimestampUtc,
    long ConfigVersion,
    HealthStatus OverallStatus,
    int RequiredFailed,
    int OptionalFailed,
    int Skipped);

public interface ISimpleHealthMetricsSink
{
    void Report(HealthMetrics metrics);
}


public static class HealthMetricsExporterExtensions
{
    public static HealthMetricsExporter StartHealthMetricsExporter(this IConfigurationHealthService health, ISimpleHealthMetricsSink sink)
        => new(health, sink);
}
