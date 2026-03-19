# Health Monitoring

Cocoar.Configuration tracks the health of every configuration rule and reports an overall status. This lets you detect misconfigurations, missing files, and stale feature flags — at startup and at runtime.

## HealthStatus

The system reports one of four states:

| Status | Meaning |
|---|---|
| `Unknown` | Not yet initialized (no rules have been evaluated) |
| `Healthy` | All rules succeeded |
| `Degraded` | One or more optional rules failed, or feature flags have expired |
| `Unhealthy` | One or more required rules failed |

```csharp
public enum HealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}
```

## How Health Is Determined

After every recompute cycle, the health tracker examines each rule's last execution outcome:

| Rule Outcome | Required Rule | Optional Rule |
|---|---|---|
| Succeeded | No effect | No effect |
| Failed | **Unhealthy** | **Degraded** |
| Skipped (via `.When()`) | No effect | No effect |

Additionally, if any [feature flag class has expired](/guide/flags/expiry-health), health reports **Degraded**.

The worst status wins: if any required rule fails, the overall status is `Unhealthy` regardless of optional rule results.

## Startup vs Runtime

Health behaves differently depending on when a failure occurs:

**During startup:**
- Required rule failures **throw immediately** — the application won't start with missing critical configuration
- Optional rule failures are recorded and health starts as `Degraded`

**At runtime (after a config change):**
- Required rule failures **roll back** the entire recompute — the last known good configuration is preserved
- Optional rule failures keep the last good value for that rule
- Health status updates to reflect the current state

## Accessing Health

### Via ConfigManager

```csharp
var manager = ConfigManager.Create(c => c.UseConfiguration(rules => [
    rules.For<AppSettings>().FromFile("appsettings.json").Required(),
    rules.For<FeatureConfig>().FromFile("features.json")
]));

// Check health status
HealthStatus status = manager.HealthStatus;
bool isHealthy = manager.IsHealthy;
```

### Via DI

In an ASP.NET Core application, use the [health check integration](/guide/health/aspnetcore) to expose health via the standard `/health` endpoint.

## Skipped Rules

Rules that are skipped via `.When()` conditions do **not** affect health. A skipped rule means the condition evaluated to `false` — the rule was intentionally not needed:

```csharp
rules.For<CloudConfig>()
    .FromFile("cloud.json")
    .When(cm => cm.GetConfig<AppSettings>()!.IsCloud)
```

If `IsCloud` is `false`, this rule is skipped and health remains `Healthy`.

## OpenTelemetry Metrics <Badge type="info" text="ADV" />

The health system emits metrics via `System.Diagnostics.Metrics`:

| Metric | Type | Description |
|---|---|---|
| `cocoar.config.health.status` | ObservableGauge | Current status (1=Healthy, 2=Degraded, 3=Unhealthy) |
| `cocoar.config.recompute.count` | Counter | Number of recompute cycles |
| `cocoar.config.recompute.duration` | Histogram (ms) | Duration of recompute cycles |
| `cocoar.config.provider.errors` | Counter | Provider failures (tags: `provider_type`, `required`) |

The meter name is `Cocoar.Configuration`. To collect these metrics, register the meter with your OpenTelemetry provider:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Cocoar.Configuration"));
```