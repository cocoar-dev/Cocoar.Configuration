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
builder.Services.AddCocoarConfiguration([
    Rule.From.File("appsettings.json").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
]);

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
