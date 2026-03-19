# Lifetimes & Registration

## Default Lifetimes

| Service | Lifetime | Why |
|---|---|---|
| Configuration types (`AppSettings`, etc.) | **Scoped** | Consistent snapshot per request (like `IOptionsSnapshot<T>`) |
| `IReactiveConfig<T>` | **Singleton** | Live subscription to changes |
| Feature flag / entitlement classes | **Singleton** | Pure functions over reactive config |
| `IFeatureFlagEvaluator` / `IEntitlementEvaluator` | **Scoped** | Needs request-scoped `IServiceProvider` for resolvers |
| Context resolvers | **Scoped** | May depend on scoped services (e.g., `DbContext`); customizable via `.AsSingleton()`, `.AsTransient()` |
| `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` | **Singleton** | Immutable metadata |

Config types are Scoped by default: each request gets a consistent snapshot from the start of that request. If the configuration changes mid-request, the change is not visible until the next request. This matches `IOptionsSnapshot<T>` behavior in Microsoft.Extensions.Options.

## Customizing Lifetimes

### AsSingleton

Register a configuration type as Singleton instead of the default Scoped:

```csharp
setup.ConcreteType<AppSettings>().AsSingleton()
```

Use this when the type has no per-request variation and you want to avoid repeated resolution.

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

Register the same configuration type under different keys (requires .NET 8+):

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

::: info Lifetime Mismatch Is Safe
Mixed lifetimes are allowed but effectively cosmetic. Both the concrete type and the exposed interface resolve the same globally-cached instance from `ConfigManager` — the DI lifetime only affects container bookkeeping (when the container would release the reference), not the actual object returned. You won't get stale data or separate instances.
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
