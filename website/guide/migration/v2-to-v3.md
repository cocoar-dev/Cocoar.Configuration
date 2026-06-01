---
description: v2 to v3 Type-First API migration — rule.File().For<T>() becomes rule.For<T>().FromFile(), config-aware .When(IConfigurationAccessor), provider-method rename table
---

# Migration v2 → v3

v3.0 introduces the **Type-First API pattern** for rule building and **config-aware conditional rules**.

## Overview

| v2.0 API (Provider-First) | v3.0 API (Type-First) |
|---|---|
| `rule.File("...").For<T>()` | `rule.For<T>().FromFile("...")` |
| `rule.Environment("...").For<T>()` | `rule.For<T>().FromEnvironment("...")` |
| `rule.StaticJson("...").For<T>()` | `rule.For<T>().FromStaticJson("...")` |
| `rule.Static<V>(factory).For<T>()` | `rule.For<T>().FromStatic(factory)` |
| `rule.Observable(obs).For<T>()` | `rule.For<T>().FromObservable(obs)` |
| `rule.HttpPolling(...).For<T>()` | `rule.For<T>().FromHttp(...)` |
| `rule.MicrosoftSource(...).For<T>()` | `rule.For<T>().FromMicrosoft(config)` |
| `.When(Func<bool>)` | `.When(Func<IConfigurationAccessor, bool>)` |

## Why Type-First?

The Type-First pattern puts the configuration type at the beginning of the chain:

- **More discoverable** — IntelliSense shows all provider options after `For<T>()`
- **Type-safe first** — the type is mandatory and declared upfront
- **Natural to read** — flows like "For AppSettings from file config.json"
- **Consistent with .NET** — similar to `services.AddScoped<T>()`, `builder.Entity<T>()`

## Quick Example

**Before (v2.0):**
```csharp
services.AddCocoarConfiguration(rule => [
    rule.File("config.json").Select("App").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

**After (v3.0):**
```csharp
services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("config.json").Select("App"),
    rule.For<AppSettings>().FromEnvironment("APP_")
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

## Migration Examples

### Simple File Rule

```csharp
// v2.0
rule.File("appsettings.json").For<AppSettings>()

// v3.0
rule.For<AppSettings>().FromFile("appsettings.json")
```

### With Select

```csharp
// v2.0
rule.File("config.json").Select("Database").For<DbSettings>()

// v3.0
rule.For<DbSettings>().FromFile("config.json").Select("Database")
```

### With Multiple Modifiers

```csharp
// v2.0
rule.File("config.json")
    .Select("Database")
    .MountAt("Db")
    .Required()
    .For<DbSettings>()

// v3.0
rule.For<DbSettings>().FromFile("config.json")
    .Select("Database")
    .MountAt("Db")
    .Required()
```

### Dynamic Rules with Accessor

```csharp
// v2.0
rule.File(accessor => {
    var tenant = accessor.GetConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenant-{tenant.Id}.json");
}).For<TenantConfig>()

// v3.0
rule.For<TenantConfig>().FromFile(accessor => {
    var tenant = accessor.GetConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenant-{tenant.Id}.json");
})
```

### Conditional Rules

The `.When()` method signature changed to receive `IConfigurationAccessor`:

```csharp
// v2.0 — When(Func<bool>)
rule.File("premium-features.json")
    .When(() => isPremium)
    .For<PremiumFeatures>()

// v3.0 — When(Func<IConfigurationAccessor, bool>)
rule.For<PremiumFeatures>().FromFile("premium-features.json")
    .When(accessor => {
        var tenant = accessor.GetConfig<TenantSettings>();
        return tenant.Tier == "Premium";
    })
```

If you were using simple conditions without configuration access, use an underscore discard:

```csharp
// v2.0
.When(() => Environment.GetEnvironmentVariable("DEBUG") == "true")

// v3.0
.When(_ => Environment.GetEnvironmentVariable("DEBUG") == "true")
```

### Environment Variables

```csharp
// v2.0
rule.Environment("APP_").For<AppSettings>()

// v3.0
rule.For<AppSettings>().FromEnvironment("APP_")
```

### Static Configuration

```csharp
// v2.0
rule.StaticJson("""{"Key": "Value"}""").For<Config>()
rule.Static(_ => new Config { Key = "Value" }).For<Config>()

// v3.0
rule.For<Config>().FromStaticJson("""{"Key": "Value"}""")
rule.For<Config>().FromStatic(_ => new Config { Key = "Value" })
```

### HTTP

```csharp
// v2.0
rule.HttpPolling(_ => new HttpPollingRuleOptions(
    urlPathOrAbsolute: "https://api.example.com/config",
    pollInterval: TimeSpan.FromMinutes(5)
)).For<RemoteConfig>()

// v3.0
rule.For<RemoteConfig>().FromHttp("https://api.example.com/config",
    pollInterval: TimeSpan.FromMinutes(5))
```

### Custom Provider

```csharp
// v2.0
rule.FromProvider<MyProvider, MyOptions, MyQuery>(
    instanceOptions: _ => new MyOptions(),
    queryOptions: _ => new MyQuery()
).For<MyConfig>()

// v3.0
rule.For<MyConfig>().FromProvider<MyConfig, MyProvider, MyOptions, MyQuery>(
    instanceOptions: _ => new MyOptions(),
    queryOptions: _ => new MyQuery()
)
```

Note: `FromProvider` now also includes the type parameter `T` for consistency.

## Automated Migration

For simple patterns, regex find/replace works:

| Pattern | Find | Replace |
|---|---|---|
| File | `rule\.File\("([^"]+)"\)\.For<([^>]+)>\(\)` | `rule.For<$2>().FromFile("$1")` |
| Environment | `rule\.Environment\("([^"]*)"\)\.For<([^>]+)>\(\)` | `rule.For<$2>().FromEnvironment("$1")` |
| StaticJson | `rule\.StaticJson\(([^)]+)\)\.For<([^>]+)>\(\)` | `rule.For<$2>().FromStaticJson($1)` |
| Observable | `rule\.Observable\(([^)]+)\)\.For<([^>]+)>\(\)` | `rule.For<$2>().FromObservable($1)` |

::: tip
For complex patterns with `.Select()`, `.When()`, `.Required()`, or dynamic accessors, manual migration is recommended to ensure correct method placement.
:::

## What Stays the Same

- Setup builder API: `setup.ConcreteType<T>().ExposeAs<I>()`
- All method modifiers (`.Select()`, `.MountAt()`, `.Required()`) work the same
- Rule execution order and semantics are identical
- Health monitoring and reactive configuration unchanged

## New in v3.0: Config-Aware Conditional Rules

The `.When()` method now receives `IConfigurationAccessor`, enabling conditional logic based on other configuration:

```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    rule.For<PremiumFeatures>().FromFile("premium.json")
        .When(accessor => {
            var tenant = accessor.GetConfig<TenantSettings>();
            return tenant.Tier == "Premium";
        }),

    rule.For<DebugSettings>().FromFile("debug.json")
        .When(_ => Environment.GetEnvironmentVariable("DEBUG_MODE") == "true")
]);
```
