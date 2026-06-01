using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cocoar.Configuration.Diagnostics;

internal static class CocoarMetrics
{
    public const string MeterName = "Cocoar.Configuration";
    internal static readonly Meter Instance = new(MeterName, "1.0.0");

    internal static readonly Counter<long> RecomputeCount = Instance.CreateCounter<long>(
        "cocoar.config.recompute.count", description: "Configuration recompute cycles");
    internal static readonly Histogram<double> RecomputeDuration = Instance.CreateHistogram<double>(
        "cocoar.config.recompute.duration", unit: "ms");
    internal static readonly Counter<long> ProviderErrors = Instance.CreateCounter<long>(
        "cocoar.config.provider.errors");
    internal static readonly Counter<long> FlagEvaluations = Instance.CreateCounter<long>(
        "cocoar.config.flags.evaluations");

    // Distributed tracing
    internal static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");
}
