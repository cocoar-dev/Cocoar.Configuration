# Health Monitoring

Cocoar.Configuration includes a **first‑class health model** for rules and recompute cycles. It produces compact snapshots you can read synchronously or stream reactively, and it exposes a tiny exporter hook for Prometheus / OpenTelemetry without taking any external dependencies.

---

## Concepts at a Glance

* **Overall status levels:** `Healthy`, `Degraded`, `Unhealthy`, `Unknown` (derived from rule results)
* **Per‑rule entries:** index, optional name, required/optional, status (`Up`, `Down`, `Skipped`, `Unknown`), timestamps, failure count, short error code/message
* **Snapshots:** monotonic `Id`, `TimestampUtc`, `ConfigVersion`, `Summary`
* **Streaming:** subscribe to snapshot changes or just to overall status changes
* **Two‑phase errors:** fail‑fast during initialization, **graceful** at runtime (preserve last good config + update health)

---

## Public API

```csharp
public interface IConfigurationHealthService
{
    HealthStatus Status { get; }
    ConfigHealthSnapshot Snapshot { get; }
    IObservable<ConfigHealthSnapshot> SnapshotStream { get; }
    IObservable<HealthStatus> StatusStream { get; }
    bool IsHealthy { get; }
}
```

* `Status` / `IsHealthy` – quick checks for liveness/health.
* `Snapshot` – latest full view (cheap, immutable).
* `SnapshotStream` – emits only meaningful changes (identical snapshots suppressed).
* `StatusStream` – emits when overall status changes.

> A `ConfigurationHealthService` implements this interface and is owned by `ConfigManager`. You typically obtain it via `configManager.GetHealthService()`.

---

## Snapshot Model (What You Get)

```csharp
public sealed class ConfigHealthSnapshot
{
    long Id;                  // strictly increasing
    DateTime TimestampUtc;    // creation time
    long ConfigVersion;       // increments only on successful recompute
    HealthStatus OverallStatus; // Healthy / Degraded / Unhealthy / Unknown
    IReadOnlyList<RuleHealthEntry> Rules; // one per rule, same order
    SummaryInfo Summary;      // quick counters
}
```

**Derivation logic** (high level):

* Any **required** rule `Down` ⇒ `Unhealthy`
* No required down, but any optional `Down` ⇒ `Degraded`
* Any `Unknown` (e.g., not yet evaluated) ⇒ `Unknown`
* Otherwise ⇒ `Healthy`

Each `RuleHealthEntry` tracks:

* `Index`, `Name?`, `Required`
* `Status` (`Up`/`Down`/`Skipped`/`Unknown`)
  - `Up`: Rule executed successfully
  - `Down`: Rule failed to execute (provider threw exception)
  - `Skipped`: Rule was skipped due to `.When()` condition returning false
  - `Unknown`: Rule has not been evaluated yet
* `LastSuccessUtc`, `LastFailureUtc`, `FailureCount`
  - **`FailureCount`** is cumulative and persistent - it increments on each failure but **never automatically resets**. This provides historical reliability metrics and enables threshold-based alerting.
* `ErrorCode?`, `ErrorMessage?` (short, provider‑specific mapping)

**Important: Optional Rule Behavior**

When an **optional rule fails** (provider throws exception, Select path missing, etc.):
- The rule status is marked as `Down` in health monitoring
- An empty JSON object `{}` is contributed to the configuration
- The resulting config type gets C# property defaults
- `LastFailureException` contains the original exception for debugging
- Application continues running with defaults while source is unavailable

This design separates **data flow** (always contributes data) from **health monitoring** (tracks issues separately), enabling graceful degradation while maintaining full observability.

The `Summary` provides aggregated counts:
* `Total`: Total number of rules
* `RequiredFailed`: Count of required rules that are `Down`
* `OptionalFailed`: Count of optional rules that are `Down`
* `Skipped`: Count of rules that were skipped

> **Note:** The `Name` field can be set explicitly using `.Named("MyRuleName")` on the rule builder. This helps identify specific rules in health snapshots.

---

## Using It in ASP.NET Core

```csharp
app.MapGet("/health", (ConfigManager manager) =>
{
    var health = manager.GetHealthService();
    return Results.Json(new {
        status = health.Status,
        version = health.Snapshot.ConfigVersion,
        summary = health.Snapshot.Summary
    });
});
```

---

## Metrics Export (Prometheus / OTEL)

> **Status: Experimental / Untested**
> Implementation exists but lacks dedicated unit tests. Validate in your environment and pin versions. Tracking issue recommended.

### Simple Prometheus Example

Access `health.Snapshot` directly to expose metrics:

```csharp
app.MapGet("/metrics", (IConfigurationHealthService health) =>
{
    var s = health.Snapshot;
    var sb = new StringBuilder();

    sb.Append("# HELP cocoar_config_health_status Overall health (0=Unknown,1=Healthy,2=Degraded,3=Unhealthy)\n");
    sb.Append("# TYPE cocoar_config_health_status gauge\n");
    sb.Append($"cocoar_config_health_status {(int)s.OverallStatus}\n\n");

    sb.Append("# HELP cocoar_config_required_failures Required rules that failed\n");
    sb.Append("# TYPE cocoar_config_required_failures gauge\n");
    sb.Append($"cocoar_config_required_failures {s.Summary.RequiredFailed}\n\n");

    sb.Append("# HELP cocoar_config_optional_failures Optional rules that failed\n");
    sb.Append("# TYPE cocoar_config_optional_failures gauge\n");
    sb.Append($"cocoar_config_optional_failures {s.Summary.OptionalFailed}\n\n");

    sb.Append("# HELP cocoar_config_version Configuration version counter\n");
    sb.Append("# TYPE cocoar_config_version counter\n");
    sb.Append($"cocoar_config_version {s.ConfigVersion}\n");

    return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
});
```

> **Note:** Use `\n` (not `\r\n`) for line endings in Prometheus text format.

### Push-based Metrics (Optional)

Subscribe to `SnapshotStream` if you need push-based updates:

```csharp
// Subscribe to health changes
health.SnapshotStream.Subscribe(snapshot => {
    // Update Datadog/StatsD/custom metrics
    myMetrics.Gauge("config.health.status", (int)snapshot.OverallStatus);
    myMetrics.Gauge("config.health.failed_required", snapshot.Summary.RequiredFailed);
    myMetrics.Gauge("config.health.failed_optional", snapshot.Summary.OptionalFailed);
});

// Or just log changes
health.SnapshotStream.Subscribe(snapshot => {
    foreach (var rule in snapshot.Rules.Where(r => r.Status == RuleResultStatus.Down))
    {
        _logger.LogError("Config rule failed: {Name} - {Error}", rule.Name, rule.ErrorMessage);
    }
});
```

---

## Where Do Snapshots Come From?

`ConfigManager` publishes a new snapshot **after every recompute**.

**For required rules:**
On runtime failures, the system **preserves the last good configuration** for all types, logs the error, rolls back the recompute, and publishes a snapshot reflecting the failure (without bumping `ConfigVersion`). Health status becomes `Unhealthy`.

**For optional rules:**
On runtime failures, the rule contributes an empty JSON object `{}` (resulting in C# defaults), the recompute continues, and a snapshot is published with that rule marked as `Down` and status `Degraded`. The `ConfigVersion` increments because a recompute completed successfully (even though one or more optional rules failed). Consumers receive a valid configuration with defaults while health monitoring tracks the degraded state.

This design separates:
- **Data flow**: Always produces valid configuration (even if defaults)
- **Health monitoring**: Tracks source availability and failures separately

---

## Error Codes (examples)

Providers map exceptions to compact codes such as:

* `FILE_NOT_FOUND`, `FILE_IO_ERROR`
* `JSON_PARSE`
* `HTTP_TIMEOUT`, `HTTP_ERROR_STATUS`

These codes show up in `RuleHealthEntry.ErrorCode` for quick triage.

---

## Provider-supplied error codes (recommended)

Central code does not try to parse exception messages. Instead, providers should attach a short, structured error code to the exception they throw using `Exception.Data`.

Contract:

* Set `ex.Data["HealthErrorCode"] = "YOUR_CODE"` before throwing or rethrowing.
* Optionally set a short message; the health tracker will use `Exception.Message` (trimmed) as `ErrorMessage`.
* If no code is provided, the health tracker will leave `ErrorCode` unset (`null`). There is no central exception-type or message parsing in the core—providers own the mapping.

Example in a provider:

```csharp
try
{
    // provider logic ...
}
catch (HttpRequestException ex)
{
    ex.Data["HealthErrorCode"] = ex.StatusCode == System.Net.HttpStatusCode.NotFound
        ? "FILE_NOT_FOUND"
        : "HTTP_ERROR_STATUS";
    throw; // health tracker will pick up the code
}
catch (JsonException ex)
{
    ex.Data["HealthErrorCode"] = "JSON_PARSE";
    throw;
}
```

This approach keeps mapping responsibility with the provider (which has full context) while ensuring a consistent, compact surface for health reporting. Suggested codes: `FILE_NOT_FOUND`, `FILE_IO_ERROR`, `JSON_PARSE`, `HTTP_TIMEOUT`, `HTTP_ERROR_STATUS`, `PROVIDER_CANCELED` — but you can define others that fit your domain.

---

## Example: Naming Rules for Better Observability

You can give rules explicit names using the `.Named()` method to make health snapshots more readable:

```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<DatabaseConfig>()
        .FromFile("db.json")
        .Required()
        .Named("Primary Database"),

    rule.For<CacheConfig>()
        .FromFile("cache.json")
        .Named("Redis Cache"),

    rule.For<ApiConfig>()
        .FromEnvironment("API_")
        .Named("API Settings")
]);
```

When you check health, rule names appear in the snapshot:

```csharp
var health = manager.GetHealthService().Snapshot;
foreach (var rule in health.Rules)
{
    Console.WriteLine($"{rule.Name ?? $"Rule {rule.Index}"}: {rule.Status}");
}
// Output:
// Primary Database: Up
// Redis Cache: Up
// API Settings: Up
```

You can also check the summary for aggregated information:

```csharp
var summary = health.Summary;
Console.WriteLine($"Total rules: {summary.Total}");
Console.WriteLine($"Skipped: {summary.Skipped}");
Console.WriteLine($"Required failed: {summary.RequiredFailed}");
Console.WriteLine($"Optional failed: {summary.OptionalFailed}");
```

### Conditional Rules and Skipped Status

Rules with `.When()` conditions that evaluate to `false` are marked as `Skipped`:

```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<FeatureConfig>()
        .FromFile("features.json")
        .When(cfg => cfg.Get<AppConfig>().EnableFeatureX)
        .Named("Feature X Config")
]);

// If EnableFeatureX is false, this rule will have Status = Skipped
var health = manager.GetHealthService().Snapshot;
var featureRule = health.Rules.FirstOrDefault(r => r.Name == "Feature X Config");
if (featureRule?.Status == RuleResultStatus.Skipped)
{
    Console.WriteLine("Feature X is disabled, config was skipped");
}
```

If no explicit name is provided, the name will be `null` and you can identify rules by their index.

---

## Notes

* Identical snapshots (same overall status, same config version, same rule status/failure counters) are **suppressed** to avoid noisy streams.
* Rule *names* are optional and can be set with `.Named("RuleName")`; index order is stable and sufficient for dashboards.
* Health is **orthogonal** to configuration retrieval; even when health is degraded, consumers keep using the last good config.

---

## Related

* [README → Health Monitoring & Reliability](../README.md#health-monitoring--reliability)
