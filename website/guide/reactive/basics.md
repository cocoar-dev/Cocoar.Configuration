---
description: "IReactiveConfig<T> : IObservable<T> — CurrentValue, Subscribe, replay-1 BehaviorSubject semantics, reference-equality change detection, atomic swap, Scoped vs Singleton"
---

# IReactiveConfig\<T\>

Every configuration type automatically gets a reactive counterpart. `IReactiveConfig<T>` lets you subscribe to live configuration changes without polling.

## The Interface

```csharp
public interface IReactiveConfig<out T> : IObservable<T>
{
    T CurrentValue { get; }
}
```

Two capabilities:

| Member | Purpose |
|---|---|
| `CurrentValue` | Synchronously returns the latest configuration snapshot |
| `Subscribe(IObserver<T>)` | Inherited from `IObservable<T>` — receive notifications on change |

`IReactiveConfig<T>` extends `IObservable<T>` from the BCL — no dependency on System.Reactive. Consumers are free to use System.Reactive, Rx.NET, or plain `IObserver<T>` on their side.

## Getting an IReactiveConfig\<T\>

### Via DI (most common)

`IReactiveConfig<T>` is registered as **Singleton** automatically for every configuration type:

```csharp
public class NotificationService(IReactiveConfig<AppSettings> config)
{
    public void Start()
    {
        config.Subscribe(settings =>
        {
            Console.WriteLine($"MaxRetries changed to {settings.MaxRetries}");
        });
    }
}
```

### Without DI

```csharp
using var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
    ]));

var reactive = manager.GetReactiveConfig<AppSettings>();
reactive.Subscribe(settings => Console.WriteLine($"Updated: {settings.AppName}"));
```

## Subscription Behavior

Subscribers receive the **current value immediately** on subscribe, then every subsequent change:

```csharp
config.Subscribe(settings =>
{
    // Called immediately with the current value
    // Called again whenever config changes
});
```

This is replay-1 / BehaviorSubject semantics — you never miss the initial state.

## Change Detection

Changes are detected by **reference equality**, not by comparing property values. Each recompute creates a new instance — so if the underlying data changed, you get a new object reference and the subscriber fires.

If the data hasn't changed (same JSON content), the same instance reference is reused and the subscriber is **not** called. This prevents unnecessary notifications.

## Concurrency & Thread Safety <Badge type="info" text="ADV" />

### Immutable Swap

When a recompute completes, the new configuration instance atomically replaces the old one via a reference swap. There is no lock, no mutex — just an atomic reference assignment (`Interlocked.Exchange`). Readers always see either the old value or the new value, never a torn or partially-updated state.

### What Happens During a Recompute

If a request starts while a recompute is in progress, it reads the **current (old) snapshot**. The recompute runs on a background thread; nothing blocks. When it finishes, the next property access or subscription callback sees the new snapshot. There is never any contention between request threads and the recompute pipeline.

### Scoped Injection

When you inject `T` directly (registered as **Scoped**), you get a snapshot that is fixed for the duration of the request. Even if a recompute completes mid-request, your injected `T` does not change — it was captured at scope creation time.

```csharp
public class OrderController(AppSettings settings) : ControllerBase
{
    // settings is a fixed snapshot — it won't change during this request,
    // even if a recompute happens while the request is in flight.
    public IActionResult Get() => Ok(settings.MaxRetries);
}
```

This is the recommended approach for request-handling code: inject `T`, not `IReactiveConfig<T>`, and enjoy a stable view of configuration throughout the request.

### IReactiveConfig\<T\>.CurrentValue

`CurrentValue` always returns the latest snapshot at the moment you read it. If you call it twice, the second call **might** return a different instance if a recompute completed between the two calls. For most use cases this is perfectly fine — the window is microseconds, and both values are individually consistent.

If you need a stable reference across multiple reads within the same scope of work, capture it once:

```csharp
var snapshot = config.CurrentValue;
// Use snapshot.Foo and snapshot.Bar — guaranteed to be from the same recompute.
```

## CurrentValue

`CurrentValue` gives synchronous access to the latest configuration without subscribing:

```csharp
public class MyService(IReactiveConfig<AppSettings> config)
{
    public string GetAppName() => config.CurrentValue.AppName;
}
```

This is useful when you need a one-off read without ongoing notifications. The value is always up-to-date — it reflects the latest recompute.

## Lifetimes

| Registration | Lifetime | Why |
|---|---|---|
| `T` (concrete config) | Scoped | Stable snapshot per request |
| `IReactiveConfig<T>` | Singleton | Continuous stream, shared across requests |

The concrete type `T` is scoped so each request sees a consistent snapshot. `IReactiveConfig<T>` is a singleton because it represents the live stream — it wouldn't make sense to create a new stream per request.

## Common Patterns

### React to changes

```csharp
public class CacheService(IReactiveConfig<CacheSettings> config) : IHostedService
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken ct)
    {
        _subscription = config.Subscribe(settings =>
        {
            ResizeCache(settings.MaxSize);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }
}
```

### Expose via interface <Badge type="info" text="ADV" />

If you used `.ExposeAs<IAppSettings>()` in setup, you can inject the reactive version via the interface:

```csharp
public class MyService(IReactiveConfig<IAppSettings> config)
{
    // Works because ExposeAs registered the interface mapping
}
```

### One-off read vs subscription

```csharp
// One-off: read current value
var current = config.CurrentValue;

// Ongoing: react to changes
config.Subscribe(newValue => { /* ... */ });
```

## Disposing Subscriptions <Badge type="info" text="ADV" />

`Subscribe()` returns an `IDisposable`. Dispose it to stop receiving notifications:

```csharp
var subscription = config.Subscribe(settings => { /* ... */ });

// Later, when done:
subscription.Dispose();
```

In hosted services or long-lived components, dispose subscriptions in your cleanup/shutdown path.
