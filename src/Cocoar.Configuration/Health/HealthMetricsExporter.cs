namespace Cocoar.Configuration.Health;

/// <summary>
/// Consumes health snapshots and forwards aggregated counters to an ISimpleHealthMetricsSink.
/// Lightweight adapter so users can plug in Prometheus, OpenTelemetry, etc. without taking dependencies here.
/// </summary>
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
        // Avoid duplicate work if the stream implementation ever emits same Id twice
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

/// <summary>
/// Simple immutable payload sent to metrics sinks; intentionally minimal.
/// </summary>
public readonly record struct HealthMetrics(
    long SnapshotId,
    DateTime TimestampUtc,
    long ConfigVersion,
    HealthStatus OverallStatus,
    int RequiredFailed,
    int OptionalFailed,
    int Skipped);

/// <summary>
/// User implements to push metrics to their system of choice.
/// </summary>
public interface ISimpleHealthMetricsSink
{
    void Report(HealthMetrics metrics);
}

/// <summary>
/// Helper factory for on-demand exporter creation.
/// </summary>
public static class HealthMetricsExporterExtensions
{
    public static HealthMetricsExporter StartHealthMetricsExporter(this IConfigurationHealthService health, ISimpleHealthMetricsSink sink)
        => new(health, sink);
}
