---
description: Health API reference — HealthStatus enum, ConfigManager.IsHealthy, IFlagsHealthSource, ASP.NET Core health check, OpenTelemetry meters and Activity source
---

# Health API Reference

## HealthStatus Enum

```csharp
namespace Cocoar.Configuration.Health;

public enum HealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}
```

| Value | Meaning |
|---|---|
| `Unknown` | Not yet initialized or no rules evaluated |
| `Healthy` | All rules healthy, no expired flags |
| `Degraded` | Optional rule failed or expired feature flags detected |
| `Unhealthy` | Required rule failed |

Worst status wins — if any rule is `Unhealthy`, the overall status is `Unhealthy` regardless of other rules.

## ConfigManager Properties

```csharp
public sealed class ConfigManager
{
    /// <summary>Current overall health status.</summary>
    public HealthStatus HealthStatus { get; }

    /// <summary>True when HealthStatus is Healthy.</summary>
    public bool IsHealthy { get; }
}
```

Access after initialization:

```csharp
var manager = ConfigManager.Create(c => c.UseConfiguration(rules));
var status = manager.HealthStatus;  // HealthStatus.Healthy
var ok = manager.IsHealthy;         // true
```

## IFlagsHealthSource

```csharp
namespace Cocoar.Configuration.Health;

public interface IFlagsHealthSource
{
    /// <summary>
    /// Returns true if any registered feature flag class has expired.
    /// </summary>
    bool HasExpiredFlags();
}
```

Implemented internally by `FeatureFlagsHealthSource`. Reads from `IFeatureFlagsDescriptors.Expired` to detect expired flag classes. Wired automatically when feature flags are registered.

## Health Determination Logic

The health tracker evaluates after each recompute:

1. **Required rules** — any failure → `Unhealthy`
2. **Optional rules** — any failure → `Degraded`
3. **Expired feature flags** — any expired → `Degraded`
4. **Unevaluated rules** — any not yet evaluated → `Unknown`
5. **All passing** → `Healthy`

Skipped rules (condition returned `false` via `.When()`) do not affect health.

## ASP.NET Core Health Check

### Registration

```csharp
public static class CocoarHealthCheckExtensions
{
    public static IHealthChecksBuilder AddCocoarConfigurationHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "cocoar-configuration",
        params string[] tags);
}
```

### Status Mapping

| Cocoar `HealthStatus` | ASP.NET Core `HealthCheckResult` |
|---|---|
| `Healthy` | `Healthy` — "All rules healthy" |
| `Degraded` | `Degraded` — includes description |
| `Unhealthy` | `Unhealthy` — includes description |
| `Unknown` | `Degraded` — "Health status unknown" |

### Example

```csharp
builder.Services.AddHealthChecks()
    .AddCocoarConfigurationHealthCheck(
        name: "cocoar-config",
        tags: ["live", "ready"]);

app.MapHealthChecks("/health");
```

## OpenTelemetry Metrics

Meter name: `Cocoar.Configuration`

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `cocoar.config.health.status` | ObservableGauge&lt;int&gt; | — | Current health: 1=Healthy, 2=Degraded, 3=Unhealthy |
| `cocoar.config.recompute.count` | Counter&lt;long&gt; | — | Configuration recompute cycles |
| `cocoar.config.recompute.duration` | Histogram&lt;double&gt; | ms | Duration of recompute cycles |
| `cocoar.config.provider.errors` | Counter&lt;long&gt; | — | Provider errors encountered |
| `cocoar.config.flags.evaluations` | Counter&lt;long&gt; | — | Feature flag evaluations |

### Subscribing to Metrics

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Cocoar.Configuration"));
```

## Activity Source

Name: `Cocoar.Configuration` (v1.0.0) — used for distributed tracing of recompute cycles.
