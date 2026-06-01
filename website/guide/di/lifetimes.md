---
description: DI lifetimes — Scoped config types, Singleton IReactiveConfig<T>, AsSingleton/AsTransient/AsScoped, keyed services, exposed-type lifetimes, deterministic ordering
---

# Lifetimes & Registration

## How Resolution Works

Understanding this is key: **DI resolution does not recompute or re-deserialize configuration.**

When you inject `AppSettings`, here's what happens:

1. DI calls the registered factory
2. Factory calls `ConfigManager.GetConfig<AppSettings>()`
3. ConfigManager returns the **already-deserialized, cached instance**

That's it. No JSON parsing, no provider calls, no computation. The `ConfigManager` holds the current instance in memory and updates it only when a provider signals a change (file modified, HTTP poll, etc.). Resolution is a dictionary lookup — effectively free.

::: tip Key Insight
**Scoped ≠ recomputed per request.** Scoped means the DI factory calls `ConfigManager.GetConfig<T>()` once per scope and caches the result. That call is a dictionary lookup — no parsing, no computation.

**This is why Scoped is the correct default.** Each scope gets the current instance at scope creation. When configuration changes, the next scope gets the new instance automatically. Switching to Singleton does **not** improve performance — it makes things worse: the factory is called once at startup, and the DI container caches that result forever. After a configuration change, Singleton consumers still see the old instance.
:::

## Default Lifetimes

| Service | Lifetime | Why |
|---|---|---|
| Configuration types (`AppSettings`, etc.) | **Scoped** | Consistent snapshot per request |
| `IReactiveConfig<T>` | **Singleton** | Live subscription to changes |
| Feature flag / entitlement classes | **Singleton** | Pure functions over reactive config |
| `IFeatureFlagEvaluator` / `IEntitlementEvaluator` | **Scoped** | Needs request-scoped `IServiceProvider` for resolvers |
| Context resolvers | **Scoped** | May depend on scoped services (e.g., `DbContext`); customizable via `.AsSingleton()`, `.AsTransient()` |
| `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` | **Singleton** | Immutable metadata |

## Injection Patterns

Choose based on what you need:

```csharp
// 1. Direct injection (most common) — stable snapshot within scope
public class OrderService(AppSettings settings)
{
    // settings is the same instance for the entire request
}

// 2. IReactiveConfig<T> — live updates, for long-lived services
public class BackgroundMonitor(IReactiveConfig<AppSettings> config)
{
    // config.CurrentValue always returns the latest
    // config.Subscribe(...) emits on every change
}

// 3. IConfigurationAccessor — access any config type dynamically
public class PluginHost(IConfigurationAccessor accessor)
{
    var db = accessor.GetConfig<DatabaseConfig>();
    var app = accessor.GetConfig<AppSettings>();
}
```

## Customizing Lifetimes

### AsSingleton

Register a configuration type as Singleton instead of the default Scoped:

```csharp
setup.ConcreteType<AppSettings>().AsSingleton()
```

The DI container calls the factory once and caches the result. **After a configuration change, Singleton consumers keep the old instance.** Only use this for config types that genuinely never change at runtime. For live updates, use `IReactiveConfig<T>` instead.

::: danger Don't use AsSingleton for "performance"
Scoped resolution is already a dictionary lookup — there is no performance benefit to Singleton. Singleton only means you miss configuration updates. If an AI tool or colleague suggests `.AsSingleton()` to "avoid repeated resolution," that's a misconception.
:::

### AsTransient

```csharp
setup.ConcreteType<AppSettings>().AsTransient()
```

A fresh instance on every resolution. Rarely needed for configuration types.

### AsScoped (explicit default)

```csharp
setup.ConcreteType<AppSettings>().AsScoped()
```

Same as the default — useful for being explicit or overriding an interface's lifetime.

## Keyed Services

Register the same configuration type under different keys (keyed services, .NET 9+):

```csharp
setup.ConcreteType<AppSettings>().AsSingleton("primary"),
setup.ConcreteType<AppSettings>().AsScoped("per-request")
```

Consume via `[FromKeyedServices]`:

```csharp
public class MyService(
    [FromKeyedServices("primary")] AppSettings primary,
    [FromKeyedServices("per-request")] AppSettings perRequest)
{ }
```

## Exposed Types

When exposing a concrete type through an interface, you can customize the interface's lifetime independently:

```csharp
setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
setup.ExposedType<IAppSettings>().AsSingleton()
```

Here `AppSettings` is Scoped (default) but `IAppSettings` is Singleton.

::: warning Lifetime Mismatch — Be Careful
If the concrete type is Scoped but the exposed interface is Singleton, the interface will resolve once and cache the startup instance forever — it **will** become stale after a config change. Keep exposed interfaces at the same lifetime as their concrete type, or explicitly choose Scoped for both.
:::

## DisableAutoRegistration

Prevent the default Scoped registration while still allowing custom registrations:

```csharp
setup.ConcreteType<AppSettings>().DisableAutoRegistration().AsSingleton()
```

This registers only the Singleton — no Scoped default is emitted.

Without `AsSingleton()`, the type wouldn't be in DI at all (but still accessible via `ConfigManager.GetConfig<T>()` directly).

## Registration Order

Service descriptors are emitted in **deterministic order** — sorted alphabetically by type full name. This means the same configuration always produces the same `IServiceCollection` contents, regardless of rule declaration order. This is important for reproducibility and debugging.

## What Gets Registered

For each configuration type, up to two services are registered:

1. **The type itself** (Scoped by default) — resolves via `ConfigManager.GetConfig<T>()`
2. **`IReactiveConfig<T>`** (always Singleton) — resolves via `ConfigManager.GetReactiveConfig<T>()`

The reactive wrapper is always registered as Singleton because it represents a live subscription, not a snapshot.
