# Migration Guide: v2.0 → v3.0

This guide explains how to migrate from v2.0 to v3.0, which introduces the **Type-First API pattern** for rule building.

## Overview

v3.0 introduces two major changes:
1. **Type-First API pattern** for rule building
2. **Config-aware `.When()` method** with `IConfigurationAccessor` parameter

### Migration Table

| v2.0 API (Provider-First) | v3.0 API (Type-First) |
|---------------------------|----------------------|
| `rule.File("...").For<T>()` | `rule.For<T>().FromFile("...")` |
| `rule.Environment("...").For<T>()` | `rule.For<T>().FromEnvironment("...")` |
| `rule.StaticJson("...").For<T>()` | `rule.For<T>().FromStaticJson("...")` |
| `rule.Static<V>(factory).For<T>()` | `rule.For<T>().FromStatic(factory)` |
| `rule.Observable(obs).For<T>()` | `rule.For<T>().FromObservable(obs)` |
| `rule.HttpPolling(...).For<T>()` | `rule.For<T>().FromHttpPolling(...)` |
| `rule.MicrosoftSource(...).For<T>()` | `rule.For<T>().FromMicrosoftSource(...)` |
| `rule.FromProvider<P,O,Q>(...).For<T>()` | `rule.For<T>().FromProvider<T,P,O,Q>(...)` |
| `.When(Func<bool>)` | `.When(Func<IConfigurationAccessor, bool>)` |

## Why Type-First?

The Type-First pattern puts the configuration type at the beginning of the chain, making the API:

- **More Discoverable**: IntelliSense shows all provider options after `For<T>()`
- **Type-Safe First**: The type is mandatory and declared upfront
- **Natural to Read**: Flows like "For AppSettings from file config.json"
- **Consistent with .NET**: Similar to DI patterns (`services.AddScoped<T>()`, `builder.Entity<T>()`)

## Quick Example

### Before (v2.0)
```csharp
services.AddCocoarConfiguration(rule => [
    rule.File("config.json").Select("App").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

### After (v3.0)
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
**Before (v2.0):**
```csharp
rule.File("appsettings.json").For<AppSettings>()
```

**After (v3.0):**
```csharp
rule.For<AppSettings>().FromFile("appsettings.json")
```

### With Select
**Before (v2.0):**
```csharp
rule.File("config.json").Select("Database").For<DbSettings>()
```

**After (v3.0):**
```csharp
rule.For<DbSettings>().FromFile("config.json").Select("Database")
```

### With Required
**Before (v2.0):**
```csharp
rule.File("config.json").Required().For<CoreConfig>()
```

**After (v3.0):**
```csharp
rule.For<CoreConfig>().FromFile("config.json").Required()
```

### With Multiple Modifiers
**Before (v2.0):**
```csharp
rule.File("config.json")
    .Select("Database")
    .MountAt("Db")
    .Required()
    .For<DbSettings>()
```

**After (v3.0):**
```csharp
rule.For<DbSettings>().FromFile("config.json")
    .Select("Database")
    .MountAt("Db")
    .Required()
```

### Dynamic Rules with Accessor
**Before (v2.0):**
```csharp
rule.File(accessor => {
    var tenant = accessor.GetRequiredConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenant-{tenant.Id}.json");
}).For<TenantConfig>()
```

**After (v3.0):**
```csharp
rule.For<TenantConfig>().FromFile(accessor => {
    var tenant = accessor.GetRequiredConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenant-{tenant.Id}.json");
})
```

### Conditional Rules
**Before (v2.0):**
```csharp
rule.File("premium-features.json")
    .When(accessor => {
        var tenant = accessor.GetRequiredConfig<TenantSettings>();
        return tenant.Tier == "Premium";
    })
    .For<PremiumFeatures>()
```

**After (v3.0):**
```csharp
rule.For<PremiumFeatures>().FromFile("premium-features.json")
    .When(accessor => {
        var tenant = accessor.GetRequiredConfig<TenantSettings>();
        return tenant.Tier == "Premium";
    })
```

**Note:** The `.When()` method signature changed in v3.0 to receive `IConfigurationAccessor`:
- **v2.0:** `.When(Func<bool> predicate)`
- **v3.0:** `.When(Func<IConfigurationAccessor, bool> predicate)`

If you were using simple conditions without configuration access:
```csharp
// v2.0
.When(() => Environment.GetEnvironmentVariable("DEBUG") == "true")

// v3.0 - use underscore to ignore the accessor parameter
.When(_ => Environment.GetEnvironmentVariable("DEBUG") == "true")
```

### Environment Variables
**Before (v2.0):**
```csharp
rule.Environment("APP_").For<AppSettings>()
rule.Environment().For<AppSettings>()  // No prefix
```

**After (v3.0):**
```csharp
rule.For<AppSettings>().FromEnvironment("APP_")
rule.For<AppSettings>().FromEnvironment()  // No prefix
```

### Static Configuration
**Before (v2.0):**
```csharp
rule.StaticJson("""{"Key": "Value"}""").For<Config>()
rule.Static(_ => new Config { Key = "Value" }).For<Config>()
```

**After (v3.0):**
```csharp
rule.For<Config>().FromStaticJson("""{"Key": "Value"}""")
rule.For<Config>().FromStatic(_ => new Config { Key = "Value" })
```

### Observable Streams
**Before (v2.0):**
```csharp
rule.Observable(myObservable).For<Config>()
rule.Observable<string>(jsonObservable).For<Config>()
```

**After (v3.0):**
```csharp
rule.For<Config>().FromObservable(myObservable)
rule.For<Config>().FromObservable<Config, string>(jsonObservable)
```

### HTTP Polling
**Before (v2.0):**
```csharp
rule.HttpPolling(_ => new HttpPollingRuleOptions(
    urlPathOrAbsolute: "https://api.example.com/config",
    pollInterval: TimeSpan.FromMinutes(5)
)).For<RemoteConfig>()
```

**After (v3.0):**
```csharp
rule.For<RemoteConfig>().FromHttpPolling(_ => new HttpPollingRuleOptions(
    urlPathOrAbsolute: "https://api.example.com/config",
    pollInterval: TimeSpan.FromMinutes(5)
))
```

### Microsoft Adapter
**Before (v2.0):**
```csharp
rule.MicrosoftSource(_ => new MicrosoftConfigurationSourceRuleOptions(
    configurationSource,
    configurationPrefix: "App"
)).For<AppSettings>()
```

**After (v3.0):**
```csharp
rule.For<AppSettings>().FromMicrosoftSource(_ => new MicrosoftConfigurationSourceRuleOptions(
    configurationSource,
    configurationPrefix: "App"
))
```

### Advanced: Custom Provider with FromProvider
**Before (v2.0):**
```csharp
rule.FromProvider<MyProvider, MyOptions, MyQuery>(
    instanceOptions: _ => new MyOptions(),
    queryOptions: _ => new MyQuery()
).For<MyConfig>()
```

**After (v3.0):**
```csharp
rule.For<MyConfig>().FromProvider<MyConfig, MyProvider, MyOptions, MyQuery>(
    instanceOptions: _ => new MyOptions(),
    queryOptions: _ => new MyQuery()
)
```

Note: The type parameter `T` is now also specified in `FromProvider<T, ...>()` for consistency.

## Automated Migration

For simple patterns, you can use regex find/replace in your code editor:

### Pattern 1: Simple File
**Find:** `rule\.File\("([^"]+)"\)\.For<([^>]+)>\(\)`  
**Replace:** `rule.For<$2>().FromFile("$1")`

### Pattern 2: Simple Environment
**Find:** `rule\.Environment\("([^"]*)"\)\.For<([^>]+)>\(\)`  
**Replace:** `rule.For<$2>().FromEnvironment("$1")`

### Pattern 3: StaticJson
**Find:** `rule\.StaticJson\(([^)]+)\)\.For<([^>]+)>\(\)`  
**Replace:** `rule.For<$2>().FromStaticJson($1)`

### Pattern 4: Observable
**Find:** `rule\.Observable\(([^)]+)\)\.For<([^>]+)>\(\)`  
**Replace:** `rule.For<$2>().FromObservable($1)`

**Note:** For complex patterns with `.Select()`, `.When()`, `.Required()`, or dynamic accessors, manual migration is recommended to ensure correct placement of methods.

## Non-DI Usage (Core Only)

### Before (v2.0)
```csharp
var manager = new ConfigManager(rule => [
    rule.File("config.json").For<AppSettings>()
]).Initialize();
```

### After (v3.0)
```csharp
var manager = new ConfigManager(rule => [
    rule.For<AppSettings>().FromFile("config.json")
]).Initialize();
```

## What Stays the Same

- Setup builder API remains unchanged: `setup.ConcreteType<T>().ExposeAs<I>()`
- All method modifiers (`.Select()`, `.MountAt()`, `.Required()`) work the same way
- Rule execution order and semantics are identical
- Health monitoring and reactive configuration features unchanged

## New Features in v3.0

### Config-Aware Conditional Rules

The `.When()` method now receives an `IConfigurationAccessor` parameter, enabling powerful conditional logic based on other configuration:

```csharp
builder.AddCocoarConfiguration(rule => [
    // Load tenant settings first
    rule.For<TenantSettings>().FromFile("tenant.json"),
    
    // Conditionally load premium features based on tenant tier
    rule.For<PremiumFeatures>().FromFile("premium.json")
        .When(accessor => {
            var tenant = accessor.GetRequiredConfig<TenantSettings>();
            return tenant.Tier == "Premium";
        }),
    
    // Load debug settings only in debug mode
    rule.For<DebugSettings>().FromFile("debug.json")
        .When(_ => Environment.GetEnvironmentVariable("DEBUG_MODE") == "true")
]);
```

This enables:
- **Multi-tenant configuration**: Different config files/sources per tenant
- **Environment-based rules**: Load configs conditionally by environment
- **Feature flags**: Enable/disable entire config sections dynamically
- **Cascading conditions**: Rules that depend on earlier configuration state

## Benefits

After migration, you'll benefit from:

1. **Better IntelliSense**: After typing `rule.For<AppSettings>().`, IntelliSense shows all available providers
2. **Clearer Intent**: The type comes first, making it obvious what configuration type you're defining
3. **Consistency**: All rules follow the same `For<T>().FromX()` pattern
4. **Type Safety**: Type parameter is required upfront, catching errors earlier

## Need Help?

If you encounter migration challenges:
- Check the [examples](../src/Examples/) - all have been updated to v3.0
- Review complex patterns in the test suite
- Consult the [API documentation](../README.md) for current patterns
