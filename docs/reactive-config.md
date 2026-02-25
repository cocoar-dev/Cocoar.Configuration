# IReactiveConfig<T>

`IReactiveConfig<T>` is Cocoar.Configuration’s reactive configuration interface. It combines two important capabilities:

* **Observable stream** – subscribe to configuration changes via `IObservable<T>`
* **Current value access** – read the latest configuration value synchronously via `CurrentValue`

This gives you both push-based reactivity and pull-based safety in one abstraction.

---

## Interface

```csharp
public interface IReactiveConfig<out T> : IObservable<T>
{
    T CurrentValue { get; }
}
```

* `CurrentValue`: Always safe to access; returns the latest known configuration instance.
* `IObservable<T>`: Subscribe to updates when configuration changes.

---

## Error Resilience

The built-in implementation ensures that reactive streams:

* Never terminate due to provider errors
* Never terminate due to subscriber exceptions
* Log and ignore faulty emissions while keeping the stream alive

This makes `IReactiveConfig<T>` **bulletproof** under real-world conditions.

---

## Usage Examples

### Dependency Injection (ASP.NET Core)

```csharp
builder.Services.AddCocoarConfiguration(c => c.WithConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),
    rule.For<AppSettings>().FromEnvironment("APP_")
]));

// Automatically registers IReactiveConfig<AppSettings>
```

Then inject it:

```csharp
app.MapGet("/settings", (IReactiveConfig<AppSettings> reactive) => new {
    current = reactive.CurrentValue,
    // subscribe to changes if needed
});
```

### Manual Subscription

```csharp
var reactive = manager.GetReactiveConfig<AppSettings>();

using var sub = reactive.Subscribe(cfg =>
{
    Console.WriteLine($"FeatureFlag changed: {cfg.FeatureFlag}");
});

Console.WriteLine($"Current value: {reactive.CurrentValue}");
```

### Interface Types

If you expose a concrete type as an interface via the setup API, you can also request `IReactiveConfig<IInterface>`:

```csharp
builder.Services.AddCocoarConfiguration(c => c.WithConfiguration(
    rule => [rule.For<AppSettings>().FromFile("appsettings.json")],
    setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]
));

// Both work:
var concrete = manager.GetReactiveConfig<AppSettings>();
var asInterface = manager.GetReactiveConfig<IAppSettings>();
```

This is useful when your services depend on interfaces rather than concrete types for testability and decoupling.

### Tuple-Based Multi-Config Snapshots (Arbitrary Arity)

Sometimes you need a consistent snapshot spanning several configuration types (e.g., `AppSettings`, `FeatureFlags`, `LoggingConfig`, `Pricing`, etc.). Naïvely combining individual `IReactiveConfig<T>` streams with `CombineLatest` risks mixing values from different recompute passes when only one type changed.

Instead of fixed multi-arity interfaces, Cocoar now supports **any ValueTuple** shape via the regular API:

```csharp
var trio = manager.GetReactiveConfig<(AppSettings, FeatureFlags, LoggingConfig)>();
var octo = manager.GetReactiveConfig<(A,B,C,D,E,F,G,H)>();
```

Properties:
* Emits **at most once per recompute pass**, and only if at least one element changed during that pass
* Elements in each emission are **atomically aligned** (all from the same recompute cycle)
* Supports tuples beyond 7 elements (uses nested ValueTuple `Rest` under the hood) transparently
* No emission spam when nothing changed

Usage:

```csharp
public sealed class CompositeHandler(IReactiveConfig<(AppSettings App, FeatureFlags Flags, LoggingConfig Log)> reactive)
{
    public object Get()
    {
        var snapshot = reactive.CurrentValue; // (App, Flags, Log)
        var (app, flags, log) = snapshot;
        return new { app.Version, Experimental = flags.Enabled.Contains("NewUI"), log.Level };
    }
}

using var sub = reactive.Subscribe(tuple => {
    var (app, flags, log) = tuple;
    Console.WriteLine($"Aligned pass -> v={app.Version} flags={flags.Enabled.Length} log={log.Level}");
});
```

#### Why Not CombineLatest?

`CombineLatest` can interleave new + stale values when only one underlying type changes—leading to logically inconsistent composite state. The tuple reactive config internally uses per-pass alignment events so each emission corresponds to a single orchestrator recompute.

#### Initial Emission Behavior

Tuple configs behave like single-type reactive configs: they only emit after a recompute pass where at least one of their member types changed. You can always access the current atomic snapshot synchronously via `.CurrentValue`.

#### Tuple Element Eligibility & Validation

Only two kinds of types are allowed as tuple elements:

1. Concrete types that have a configuration rule (i.e. appear as a `.For<ConcreteType>()` in your rules).
2. Interfaces exposed by a concrete type via the Configure API: `setup.ConcreteType<Concrete>().ExposeAs<IMyInterface>()`.

If any tuple element is not one of those, construction of `IReactiveConfig<(...)>` throws immediately with a detailed message listing each invalid element and why it is invalid ("not a configured type" or "interface not exposed"). This prevents silent default/null values inside composite snapshots.

Example failure:

```
Cannot create IReactiveConfig<(PricingConfig, IUnboundFeature, int)>. The following tuple element types are not configured/bound: IUnboundFeature (interface not bound), Int32 (not a configured type)
```

This guard ensures tuple snapshots are always composed exclusively of legitimate, fully managed configuration types.

---

---

## Why It Matters

* **Immediate access** – use `CurrentValue` anywhere without waiting for async emissions
* **Reactive integration** – naturally fits with Rx, Observables, and async pipelines
* **Error resilient** – ensures your config stream never dies in production
* **Auto-registered** – no boilerplate DI setup; every config type is available automatically

---

## Related

* [Quickstart](../README.md#quickstart)
* [Features](../README.md#-features)


