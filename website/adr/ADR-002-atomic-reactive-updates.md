---
description: Transactional all-or-nothing recompute with tuple-reactive subscriptions and reference-equality change detection; no IOptionsMonitor partial-update races
---

# ADR-002: Atomic Reactive Configuration Updates

**Status:** Accepted  
**Date:** 2024-09-14  
**Decision Makers:** Core Team  
**Related:** ADR-001 (Capabilities System)

---

## Context

Configuration in distributed systems has a fundamental challenge: **how do you notify subscribers of changes across multiple related configuration types without exposing them to inconsistent state?**

### The Problem: Partial Updates

Consider a typical application with related configurations:

```csharp
public class AppSettings
{
    public string ApiUrl { get; set; }
    public int Timeout { get; set; }
}

public class DatabaseSettings
{
    public string ConnectionString { get; set; }
    public int PoolSize { get; set; }
}
```

When a configuration file changes that affects **both** types, subscribers need to see them update **atomically**. Seeing new `AppSettings` with old `DatabaseSettings` could cause:

- API calls to new endpoints with old timeouts
- Database connections with mismatched pool sizes
- Race conditions during the update window
- Unpredictable behavior in dependent services

### Why Standard Patterns Fail

**Microsoft's IOptionsMonitor&lt;T>:**

```csharp
services.Configure<AppSettings>(config.GetSection("App"));
services.Configure<DatabaseSettings>(config.GetSection("Database"));

// In your service:
_appMonitor.OnChange(newApp => { /* update */ });
_dbMonitor.OnChange(newDb => { /* update */ });
```

**Problems:**
- ❌ Two separate notification streams - no atomicity guarantee
- ❌ Observer for `AppSettings` fires before `DatabaseSettings` is ready
- ❌ Time window where state is inconsistent
- ❌ Manual coordination required across monitors
- ❌ Race conditions in multi-threaded scenarios

**System.Reactive (Rx) CombineLatest:**

```csharp
Observable.CombineLatest(
    appObservable,
    dbObservable,
    (app, db) => (app, db))
```

**Problems:**
- ❌ Emits on **any** source change, even if only one changed
- ❌ No transaction semantics - can see partial source updates
- ❌ No rollback on failure
- ❌ Complex subscription management

### Real-World Impact

**Without Atomic Updates (IOptionsMonitor):**

```
Time: 0ms   → File changes (both App + DB sections modified)
Time: 5ms   → AppSettings reloads → Observer 1 fires
Time: 10ms  → Service uses new App + OLD Db ❌ INCONSISTENT
Time: 15ms  → DatabaseSettings reloads → Observer 2 fires
Time: 20ms  → Service uses new App + new Db ✓ Consistent again
```

**Window of Inconsistency:** 10ms where state is partially updated.

**With Atomic Updates (Cocoar.Configuration):**

```
Time: 0ms   → File changes
Time: 5ms   → Recompute transaction begins
            → AppSettings computes
            → DatabaseSettings computes
            → Both commit atomically
Time: 15ms  → Single emission with (newApp, newDb) ✓ ATOMIC
```

**No inconsistency window.** Subscribers **never** see partial state.

---

## Decision

We implement **tuple-reactive atomic updates** using a transaction-based recomputation pipeline with the following guarantees:

### 1. Transactional Recompute

All configuration changes process as an **all-or-nothing transaction**:

```csharp
// Recompute Pipeline
BeginTransaction()
  ├─ Rule 1: Compute AppSettings
  ├─ Rule 2: Compute DatabaseSettings
  ├─ Rule 3: Compute CacheSettings
  └─ CommitOrRollback()
```

**If any required rule fails:**
- ❌ Entire transaction rolls back
- ✅ Consumers keep previous good configuration
- ✅ **Zero emissions** - no observer is notified
- ✅ Health status → Unhealthy

**If all rules succeed:**
- ✅ All changes commit atomically
- ✅ **Single emission** with new snapshot
- ✅ All subscribers see consistent state
- ✅ Health status → Healthy

### 2. Tuple-Reactive API

Consumers can subscribe to **multiple configurations atomically**:

```csharp
public class MyService
{
    public MyService(IReactiveConfig<(AppSettings, DatabaseSettings, CacheSettings)> config)
    {
        config.Subscribe(tuple =>
        {
            var (app, db, cache) = tuple;
            
            // GUARANTEED: All three are from the same recompute pass
            // GUARANTEED: No partial updates
            // GUARANTEED: If one changed, this fires; if all unchanged, no emission
            
            RebuildClient(app, db, cache);
        });
    }
}
```

### 3. Reference-Equality Change Detection

Only emit when configuration **actually changes**. Each recompute produces fresh
config instances; the engine compares the new per-type **instance reference**
against the last published reference and suppresses emission when they are
reference-equal:

```csharp
// Recompute produces new snapshot
oldSnapshot = { AppSettings: v1, DatabaseSettings: v1 }
newSnapshot = { AppSettings: v2, DatabaseSettings: v1 }  // Only App got a new instance

// Per-Type Change Detection (DistinctUntilChanged with ReferenceEquals):
- ReferenceEquals(AppSettings v2, AppSettings v1) == false → Changed
- ReferenceEquals(DatabaseSettings v1, DatabaseSettings v1) == true → Unchanged

// Emission:
- IReactiveConfig<AppSettings> → Emits (reference changed)
- IReactiveConfig<DatabaseSettings> → No emission (reference unchanged)
- IReactiveConfig<(AppSettings, DatabaseSettings)> → Emits (tuple member changed)
```

**Benefits:**
- Avoids spurious emissions on non-changes
- Subscribers only react when a type gets a new instance
- O(1) reference comparison — no hashing or serialization on the emit path
  (`MasterBackplane.CreateTypeProjection` ends in `.DistinctUntilChanged(ReferenceEqualityComparer<T>.Instance)`)

---

## Implementation

### Core Components

**1. MasterBackplane** (single source of truth)

`MasterBackplane` holds the current `ConfigSnapshot` in a `SimpleBehaviorSubject`
and atomically publishes new snapshots. Per-type reactive consumers subscribe to
a **type projection** built lazily over that snapshot stream. The projection
selects the type out of each snapshot and gates emissions by reference equality:

```csharp
internal sealed class MasterBackplane : IDisposable
{
    private readonly SimpleBehaviorSubject<ConfigSnapshot> _snapshotSubject;
    private readonly ConcurrentDictionary<Type, object> _typeProjectionCache = new();

    // Atomic publish: all type projections update from a single snapshot
    public void Publish(ConfigSnapshot snapshot) => _snapshotSubject.OnNext(snapshot);

    private IObservable<T> CreateTypeProjection<T>() where T : class =>
        _snapshotSubject
            .Select(snapshot => snapshot.GetConfig<T>() /* + interface mapping */)
            .Where(config => config != null)
            // Uses ReferenceEquals — no hashing on the emit path
            .DistinctUntilChanged(ReferenceEqualityComparer<T>.Instance);
}
```

**2. ReactiveConfigManager** (wrapper cache over the backplane)

Holds the `MasterBackplane` plus a single `_reactiveConfigs` dictionary of
per-type wrappers. `GetReactiveConfig<T>` returns a cached, backplane-backed
`BackplaneReactiveConfig<T>` whose `CurrentValue` reads from the backplane and
whose `Subscribe` forwards to the type projection:

```csharp
internal sealed class ReactiveConfigManager : IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _reactiveConfigs = new();
    private MasterBackplane? _backplane;

    public IReactiveConfig<T> GetReactiveConfig<T>(Func<T> fallbackAccessor) where T : class =>
        (IReactiveConfig<T>)_reactiveConfigs.GetOrAdd(
            typeof(T), _ => new BackplaneReactiveConfig<T>(_backplane!));

    private sealed class BackplaneReactiveConfig<T> : IReactiveConfig<T>, IDisposable where T : class
    {
        private readonly MasterBackplane _backplane;
        private readonly IObservable<T> _observable;

        public BackplaneReactiveConfig(MasterBackplane backplane)
        {
            _backplane = backplane;
            _observable = backplane.GetTypeProjection<T>();
        }

        public T CurrentValue => _backplane.GetConfig<T>() ?? throw new InvalidOperationException(...);
        public IDisposable Subscribe(IObserver<T> observer) => _observable.Subscribe(observer);
    }
}
```

There are no per-type subjects, hash dictionaries, or per-pass subjects — a
single snapshot subject plus reference-equality projections provide change
detection.

**3. Tuple Reactive Factory**

Handles flattening of nested tuples for atomic subscriptions. The factory
reflects over the `ValueTuple` fields to discover the element types (recursing
into `Rest` for tuples larger than 7), validates each element is a configured /
exposed type, primes each element's reactive config, then instantiates a
`ReactiveTupleConfig<>` over the same `MasterBackplane`. There is no
`Observable.CombineLatest` — the tuple reads all members from one atomic
snapshot:

```csharp
internal class ReactiveConfigurationFactory(/* ... */)
{
    private object CreateTupleReactiveConfig(Type tupleType)
    {
        var elementTypes = FlattenTuple(tupleType).ToArray();   // reflection-flatten

        // Validate + prime each distinct element's reactive config
        foreach (var et in elementTypes.Distinct()) { /* prime element type */ }

        // One ReactiveTupleConfig over the backplane — all members from one snapshot
        var generic = typeof(ReactiveTupleConfig<>).MakeGenericType(tupleType);
        return Activator.CreateInstance(
            generic, accessor, backplaneAccessor(), reactiveConfigManager, logger, bindingRegistry)!;
    }
}
```

### Atomic Recompute Flow

```
1. Change Detection
   └─ Provider signals change (file modified, HTTP poll, etc.)

2. Recompute Transaction
   ├─ ConfigurationEngine.BeginUpdate()
   ├─ Execute all rules sequentially
   ├─ Build candidate snapshot
   └─ Decision:
       ├─ Success → CommitUpdate(snapshot)
       └─ Failure → RollbackUpdate()

3. Change Detection (on success only)
   ├─ Publish the new snapshot to the MasterBackplane
   ├─ For each registered type projection:
   │   ├─ Select the type's new instance from the snapshot
   │   ├─ Compare with last published reference (ReferenceEquals)
   │   └─ If reference changed → emit
   └─ Emit changed types atomically

4. Subscriber Notification
   ├─ Single-type: Emits if that type changed
   ├─ Tuple-type: Emits if ANY member changed
   └─ All emissions use same snapshot (atomic guarantee)
```

---

## Consequences

### Positive

✅ **Atomic Consistency**: Subscribers **never** see partial updates  
✅ **Transactional Safety**: Failed recomputes don't corrupt state  
✅ **Type-Safe**: Compile-time checked tuple subscriptions  
✅ **Reference-Equality Efficiency**: O(1) change detection, no spurious emissions on non-changes  
✅ **Flexible Granularity**: Subscribe to single types or tuples  
✅ **Automatic Rollback**: Errors preserve last known good state  
✅ **Zero External Dependencies**: `IReactiveConfig<T> : IObservable<T>` uses only BCL types — no System.Reactive in shipped packages  
✅ **Observable by Design**: Configuration as first-class reactive stream  

### Trade-offs

⚠️ **Complexity**: A backplane plus per-type projections and tuple flattening (justified by correctness)  
⚠️ **Memory**: One snapshot subject plus one cached wrapper/projection per type — a single dictionary, not dual  
⚠️ **Tuple Limitation**: C# supports tuples up to 8 members (combine with nesting if needed)  
⚠️ **Reflection**: Tuple flattening uses reflection (results are cached per type)  

**Why Complexity Is Acceptable:**

- Atomic guarantees are **non-negotiable** for correctness
- Memory overhead is **negligible** (one wrapper/projection per type)
- Reference-equality change detection is **O(1)** — no hashing or serialization on the emit path
- Alternative (IOptionsMonitor) has **unfixable race conditions**

### Negative

❌ **Learning Curve**: Developers must understand tuple-reactive pattern  
❌ **Debugging**: Rx stack traces can be difficult to follow  
❌ **Expression Trees**: Tuple factory uses reflection (cached, but still indirection)  

**Mitigation:**
- Comprehensive documentation in this ADR
- Examples demonstrating both single-type and tuple usage
- Health monitoring integration for observability

---

## Alternatives Considered

### Alternative 1: Manual Coordination with IOptionsMonitor

```csharp
private AppSettings? _app;
private DatabaseSettings? _db;
private bool _appReady, _dbReady;

_appMonitor.OnChange(newApp => {
    _app = newApp;
    _appReady = true;
    TryRebuild();
});

_dbMonitor.OnChange(newDb => {
    _db = newDb;
    _dbReady = true;
    TryRebuild();
});

void TryRebuild() {
    if (_appReady && _dbReady) {
        RebuildClient(_app, _db);
        _appReady = _dbReady = false;
    }
}
```

**Rejected because:**
- Boilerplate for every subscriber
- Race conditions (what if only one changes?)
- No transactional rollback
- No hash-based change detection
- Doesn't scale to 3+ types

### Alternative 2: Polling with Locks

```csharp
private readonly object _lock = new();
private (AppSettings, DatabaseSettings) _snapshot;

// Poll every 100ms
while (true) {
    var newApp = LoadApp();
    var newDb = LoadDb();
    
    lock (_lock) {
        _snapshot = (newApp, newDb);
    }
    
    await Task.Delay(100);
}
```

**Rejected because:**
- Wastes CPU cycles polling
- 100ms latency for changes
- No reactive push notifications
- Still no atomicity guarantee (lock only helps readers)

### Alternative 3: Rx CombineLatest (Naive)

```csharp
Observable.CombineLatest(
    appObservable,
    dbObservable,
    (app, db) => (app, db))
```

**Rejected because:**
- Emits on **every** source change (spurious emissions)
- No hash-based change detection
- No transactional rollback on failure
- Doesn't integrate with our recompute pipeline

### Alternative 4: Event Sourcing

```csharp
public record ConfigChanged(Type ConfigType, object NewValue, long Version);

// Emit events, rebuild snapshots
```

**Rejected because:**
- Massive architectural change
- Requires event store
- Overkill for configuration (not business events)
- No built-in Rx integration

---

## Usage Examples

### Example 1: Single Configuration (Change-Based)

```csharp
public class ApiClient
{
    public ApiClient(IReactiveConfig<AppSettings> config, ILogger logger)
    {
        config.Subscribe(newSettings =>
        {
            logger.LogInformation("API config changed: {Url}", newSettings.ApiUrl);
            RebuildClient(newSettings);
        });
    }
}
```

**Behavior:**
- Emits **only when AppSettings value changes** (hash-based)
- No emission if recompute produces same AppSettings
- Automatic on initialization (BehaviorSubject)

### Example 2: Atomic Multi-Config (Tuple)

```csharp
public class DatabasePool
{
    public DatabasePool(
        IReactiveConfig<(AppSettings App, DatabaseSettings Db, CacheSettings Cache)> config,
        ILogger logger)
    {
        config.Subscribe(tuple =>
        {
            var (app, db, cache) = tuple;
            
            logger.LogInformation(
                "Config changed atomically: {AppUrl}, {DbConn}, {CacheTtl}",
                app.ApiUrl, db.ConnectionString, cache.TtlSeconds);
            
            // GUARANTEED: All three are consistent (same recompute pass)
            RebuildPool(app, db, cache);
        });
    }
}
```

**Behavior:**
- Emits **when any member changes**
- **All members are from the same snapshot** (atomic)
- If only `AppSettings` changed, still get all three (but only `AppSettings` has a new reference)

### Example 3: Health Monitoring Integration

```csharp
public class ConfigHealthService
{
    public ConfigHealthService(
        IReactiveConfig<(AppSettings, DatabaseSettings)> config,
        ConfigManager configManager)
    {
        // Monitor config changes
        config.Subscribe(tuple =>
        {
            var (app, db) = tuple;
            ValidateConsistency(app, db);
        });

        // Check recompute health after a change
        config.Subscribe(_ =>
        {
            if (configManager.HealthStatus == HealthStatus.Unhealthy)
            {
                AlertOps("Configuration recompute failed!");
            }
        });
    }
}
```

`ConfigManager` exposes the current health as `HealthStatus`
(`Unknown`/`Healthy`/`Degraded`/`Unhealthy`) plus the `IsHealthy` convenience
flag. A failed required rule leaves the last good configuration in place and sets
`HealthStatus` to `Unhealthy`.

---

## Performance Characteristics

### Benchmarks (Typical Scenario)

**Recompute Transaction:**
- 3 rules (File + Env + HTTP)
- 3 config types
- Total time: ~50-200ms (dominated by HTTP polling)

**Change Detection:**
- Reference comparison per type (`DistinctUntilChanged(ReferenceEquals)`): O(1), effectively free
- No hashing or serialization on the emit path
- **Negligible** compared to provider I/O

**Emission Overhead:**
- Subject.OnNext: ~10-50 microseconds per subscriber
- 10 subscribers: ~100-500 microseconds total
- **Trivial** overhead

**Memory per Type:**
- One cached `BackplaneReactiveConfig<T>` wrapper + its type projection
- No per-type hash storage, no per-pass subject
- A single shared snapshot subject backs all types

**Typical app (10 config types, 20 subscribers):**
- Memory: one snapshot subject + ~10 cached wrappers/projections = negligible
- Recompute time: ~50-200ms (provider I/O)
- Change detection: O(1) reference compares (effectively free)
- Emission time: ~1-10ms (notification)

**Conclusion:** Performance overhead is **negligible** compared to correctness benefits.

---

## Testing Strategy

**Unit Tests:**
- Atomic emission on multi-config change
- No emission when a type's instance reference is unchanged
- Rollback on required rule failure

**Integration Tests:**
- File change triggers atomic tuple update
- Failed HTTP poll rolls back entire transaction
- Concurrent subscribers see same snapshot

**Property Tests:**
- No subscriber ever sees partial state
- A type emits if and only if it gets a new instance reference
- Transaction never commits partial updates

---

## Migration Notes

### From IOptionsMonitor (Microsoft)

**Before:**
```csharp
public class MyService(
    IOptionsMonitor<AppSettings> appMonitor,
    IOptionsMonitor<DatabaseSettings> dbMonitor)
{
    private AppSettings? _app;
    private DatabaseSettings? _db;
    
    public MyService(...)
    {
        appMonitor.OnChange(a => _app = a);  // Separate notifications
        dbMonitor.OnChange(d => _db = d);     // Race condition risk
    }
}
```

**After:**
```csharp
public class MyService(
    IReactiveConfig<(AppSettings, DatabaseSettings)> config)  // Atomic tuple
{
    public MyService(...)
    {
        config.Subscribe(tuple =>
        {
            var (app, db) = tuple;  // Always consistent
            RebuildState(app, db);
        });
    }
}
```

### From System.Reactive (CombineLatest)

**Before:**
```csharp
Observable.CombineLatest(
    appObservable.DistinctUntilChanged(),  // Manual change detection
    dbObservable.DistinctUntilChanged(),
    (app, db) => (app, db))
    .Subscribe(tuple => RebuildState(tuple.app, tuple.db));
```

**After:**
```csharp
config.Subscribe(tuple =>
{
    var (app, db) = tuple;  // Built-in reference-equality change detection
    RebuildState(app, db);
});
```

---

## Future Enhancements

The following are aspirational sketches — **none are implemented yet**.

**1. Snapshot Diffing API**

```csharp
config.ObserveDiffs().Subscribe(diff =>
{
    Console.WriteLine($"Changed properties: {diff.ChangedPaths}");
});
```

**2. Conditional Subscriptions**

```csharp
config.SubscribeWhen(tuple => tuple.App.IsFeatureEnabled, tuple =>
{
    // Only fires when condition is true AND value changed
});
```

**3. Backpressure Control**

```csharp
config.Sample(TimeSpan.FromSeconds(1))  // At most once per second
    .Subscribe(tuple => /* ... */);
```

---

## References

### Internal
- `Core/MasterBackplane.cs` - Snapshot subject + per-type reference-equality projections
- `Reactive/ReactiveConfigManager.cs` - Backplane-backed wrapper cache (`BackplaneReactiveConfig<T>`)
- `Reactive/ReactiveConfigurationFactory.cs` - Tuple flattening (reflection) over the backplane
- `Reactive/ReactiveTupleConfig.cs` - Tuple wrapper
- `Core/ConfigurationEngine.cs` - Recompute transaction (BeginUpdate/CommitUpdate/RollbackUpdate)

### External
- [System.Reactive Documentation](https://github.com/dotnet/reactive)
- [Rx Design Guidelines](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/DesignGuidelines.md)
- [BehaviorSubject Semantics](https://reactivex.io/documentation/subject.html)

### Articles
- [Reactive Configuration Part 1](https://dev.to/bwi/reactive-strongly-typed-configuration-in-net-introducing-cocoarconfiguration-v30-3gbn)
- [Config-Aware Rules Part 2](https://dev.to/bwi/config-aware-rules-in-net-the-power-feature-of-cocoarconfiguration-part-2-2ibk)

---

## Conclusion

The Reactive System's complexity is **intentional and necessary** to provide atomic consistency guarantees that are impossible with standard patterns like `IOptionsMonitor<T>`.

**Key Insight:**  
Configuration changes are **transactions**, not isolated events. Subscribers must see consistent snapshots or risk undefined behavior.

**The Alternative:**  
Manual coordination with `IOptionsMonitor<T>` is error-prone, doesn't scale, and fundamentally cannot provide atomicity guarantees.

**The Decision:**  
Accept ~250 lines of well-tested reactive infrastructure to provide **bulletproof atomic updates** that work correctly in production under concurrent load.

---

**Status:** ✅ Accepted and Implemented  
**Complexity Justified:** Yes - Atomic consistency is non-negotiable  
**Next Review:** If alternative patterns emerge that provide atomicity without complexity


## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2024-09-14 | 1.0 | Initial ADR documenting atomic reactive design | Core Team |

---
