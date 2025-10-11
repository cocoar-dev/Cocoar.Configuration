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
* `LastSuccessUtc`, `LastFailureUtc`, `FailureCount`
* `ErrorCode?`, `ErrorMessage?` (short, provider‑specific mapping)

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

A minimal exporter consumes the snapshot stream and forwards **aggregated counters** to any sink you provide.

```csharp
public interface ISimpleHealthMetricsSink
{
    void Report(HealthMetrics metrics);
}

public readonly record struct HealthMetrics(
    long SnapshotId,
    DateTime TimestampUtc,
    long ConfigVersion,
    HealthStatus OverallStatus,
    int RequiredFailed,
    int OptionalFailed,
    int Skipped);

// Start exporting:
var health = manager.GetHealthService();
using var exporter = health.StartHealthMetricsExporter(mySink);
```

Write a tiny `ISimpleHealthMetricsSink` that updates your Prometheus gauges or emits OTEL metrics.

> **Planned tests:** constructor wiring, snapshot coalescing, sink exceptions resilience, correct counters on rule status transitions.
> *See GitHub issue: TBD — "Add tests for HealthMetricsExporter"*

---

## Where Do Snapshots Come From?

`ConfigManager` publishes a new snapshot **after every recompute**. On runtime failures, it **preserves the last good configuration**, logs the error, and publishes a snapshot reflecting the failure (without bumping `ConfigVersion`).

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

## Notes

* Identical snapshots (same overall status, same config version, same rule status/failure counters) are **suppressed** to avoid noisy streams.
* Rule *names* are optional; index order is stable and sufficient for dashboards.
* Health is **orthogonal** to configuration retrieval; even when health is degraded, consumers keep using the last good config.

---

## Related

* [README → Health Monitoring & Reliability](../README.md#health-monitoring--reliability)
