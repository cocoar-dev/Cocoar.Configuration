# Reactive Tuples

When multiple configuration types need to stay in sync, use `IReactiveConfig<(T1, T2)>`. All types update together — you never see a mix of old and new values.

## The Problem

Subscribing to two `IReactiveConfig<T>` instances independently creates a race condition:

```csharp
// Dangerous: A and B can be from different snapshots
config1.Subscribe(a => { /* new A, but B might still be old */ });
config2.Subscribe(b => { /* new B, but A might still be old */ });
```

If `AppSettings` and `FeatureFlags` change in the same recompute, independent subscriptions can fire at different times, giving you an inconsistent view.

## The Solution

Request a tuple:

```csharp
public class MyService(IReactiveConfig<(AppSettings App, FeatureFlags Flags)> config)
{
    public void Start()
    {
        config.Subscribe(tuple =>
        {
            var (app, flags) = tuple;
            // Both are guaranteed from the same snapshot
            Console.WriteLine($"{app.AppName}, Experiments={flags.EnableExperiments}");
        });
    }
}
```

The tuple only emits when **all** elements are present and **at least one** has changed. Both values are from the same atomic snapshot.

## How It Works

1. The engine publishes a new snapshot with all configuration types at once
2. The tuple subscription listens to the snapshot stream (not individual type streams)
3. On each snapshot, it extracts all requested types
4. If any element changed (reference equality check per element), it emits the full tuple
5. If nothing changed, no emission

This gives you an atomic, consistent view across multiple types.

## CurrentValue

Access the current tuple synchronously:

```csharp
var (app, flags) = config.CurrentValue;
```

Both values are from the same snapshot.

## Supported Arities

Tuples support 2 to 8+ elements using C# value tuples:

```csharp
// 2 elements
IReactiveConfig<(AppSettings, DatabaseConfig)>

// 3 elements
IReactiveConfig<(AppSettings, DatabaseConfig, FeatureFlags)>

// Named elements (recommended for readability)
IReactiveConfig<(AppSettings App, DatabaseConfig Db, FeatureFlags Flags)>
```

For more than 7 elements, C# uses nested `ValueTuple` with a `Rest` field — this is handled automatically.

## DI Registration

Tuple reactive configs are **registered automatically**. No explicit setup needed — just inject the type you want:

```csharp
public class MyService(IReactiveConfig<(AppSettings, FeatureFlags)> config)
{
    // Works out of the box if both AppSettings and FeatureFlags have rules
}
```

If any element type has no rules defined, you'll get an `InvalidOperationException` at resolution time.

## Without DI

```csharp
using var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
        rule.For<FeatureFlags>().FromFile("features.json"),
    ]));

var reactive = manager.GetReactiveConfig<(AppSettings, FeatureFlags)>();
reactive.Subscribe(tuple =>
{
    var (app, flags) = tuple;
    Console.WriteLine($"{app.AppName}, Experiments={flags.EnableExperiments}");
});
```

## When to Use Tuples

| Scenario | Use |
|---|---|
| Independent reaction to one type | `IReactiveConfig<T>` |
| Two+ types must stay in sync | `IReactiveConfig<(T1, T2)>` |
| Decision depends on multiple configs | `IReactiveConfig<(T1, T2, T3)>` |
| One-off read of a single type | Inject `T` directly (scoped) |

Tuples add minimal overhead. The snapshot is already atomic — the tuple just projects multiple types from it in one emission.
