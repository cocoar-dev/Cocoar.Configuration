# DI Setup

`AddCocoarConfiguration()` registers everything into Microsoft.Extensions.DependencyInjection — configuration types, reactive wrappers, feature flags, and entitlements.

::: info Package
Requires `Cocoar.Configuration.DI` (or `Cocoar.Configuration.AspNetCore`, which includes it).
:::

## Basic Registration

```csharp
builder.Services.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json"),
        rules.For<DbConfig>().FromFile("db.json")
    ]));
```

Every type from your rules is automatically registered:
- `AppSettings` and `DbConfig` as **Scoped** services (one snapshot per request)
- `IReactiveConfig<AppSettings>` and `IReactiveConfig<DbConfig>` as **Singleton** services (live updates)

No setup lambda needed — auto-registration handles the defaults.

## Setup Options

The optional setup lambda is for when you need to **customize** registration — expose interfaces, change lifetimes, or disable auto-registration. If the defaults work, skip it.

### ConcreteType

`ConcreteType<T>()` is the entry point for customizing a type's registration. On its own it does nothing beyond the default — it's useful as a starting point to chain further options:

```csharp
setup.ConcreteType<AppSettings>().AsSingleton()
```

### ExposeAs

Expose a concrete type through an interface:

```csharp
setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
```

This registers both `AppSettings` and `IAppSettings` — the interface resolves to the same instance within a scope.

### Interface with DeserializeTo

When your configuration model uses an interface property, tell the deserializer which concrete type to use:

```csharp
setup.Interface<IStorageConfig>().DeserializeTo<AzureBlobConfig>()
```

### DisableAutoRegistration

Prevent a type from being registered in DI while still keeping its rules:

```csharp
setup.ConcreteType<InternalConfig>().DisableAutoRegistration()
```

The type is loaded and available via `ConfigManager.GetConfig<T>()`, but not injected via DI. Note that `IReactiveConfig<InternalConfig>` is still registered as Singleton — only the concrete type registration is disabled.

## With Feature Flags

```csharp
builder.Services.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ])
    .UseFeatureFlags(
        flags => [flags.Register<AppFlags>()],
        resolvers => [resolvers.Global<UserByIdResolver>()])
    .UseEntitlements(
        entitlements => [entitlements.Register<PlanEntitlements>()]));
```

This additionally registers:
- `AppFlags` as Singleton
- `PlanEntitlements` as Singleton
- `IFeatureFlagsDescriptors` and `IEntitlementsDescriptors` as Singleton
- `IFeatureFlagEvaluator` and `IEntitlementEvaluator` as Scoped
- All context resolvers as Scoped (customizable via `.AsSingleton()`, `.AsTransient()`)

## With Secrets

```csharp
builder.Services.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [...])
    .UseSecretsSetup(s => s.WithX509Certificate("certs/config.pfx")));
```

## Pre-Built ConfigManager <Badge type="info" text="ADV" />

If you need to create the `ConfigManager` separately (e.g., for use before DI is available):

```csharp
var manager = ConfigManager.Create(c => c.UseConfiguration(rules => [...]));

// Use manager directly...

// Then register it
builder.Services.AddCocoarConfiguration(manager);
```

## Duplicate Prevention <Badge type="info" text="ADV" />

`AddCocoarConfiguration()` can only be called once per `IServiceCollection`. A second call throws `InvalidOperationException`. This prevents accidental double-registration from multiple startup paths.
