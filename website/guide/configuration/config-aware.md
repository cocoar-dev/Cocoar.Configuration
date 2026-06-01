---
description: Rules that read earlier results via IConfigurationAccessor (GetConfig/TryGetConfig) to derive dynamic file paths and HTTP endpoints, with COCFG002 order enforcement
---

# Config-Aware Rules

Rules can read the results of earlier rules to make decisions. This turns the rule list into a pipeline where configuration drives configuration.

## The Idea

Consider a multi-tenant application. You load tenant settings first, then use the tenant's region to determine which API endpoint to poll:

```csharp
rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    rule.For<ApiConfig>().FromHttp(accessor =>
    {
        var tenant = accessor.GetConfig<TenantSettings>();
        return new HttpRuleOptions(
            $"https://{tenant.Region}.api.example.com/config",
            pollInterval: TimeSpan.FromMinutes(5));
    }),
]
```

The second rule doesn't hardcode a URL — it derives it from `TenantSettings`, which was loaded by the first rule. When the tenant file changes and the region changes, the HTTP rule automatically switches endpoints.

## IConfigurationAccessor

Every rule factory receives an `IConfigurationAccessor`. This interface provides access to configuration from all rules that have already executed:

```csharp
public interface IConfigurationAccessor
{
    T? GetConfig<T>() where T : class;
    bool TryGetConfig<T>(out T? value) where T : class;
}
```

| Method | Behavior |
|---|---|
| `GetConfig<T>()` | Returns the config instance. Throws if no rule is registered for `T`. |
| `TryGetConfig<T>()` | Returns `true` and the instance if available, `false` otherwise. |

::: warning Rule Order Matters
The accessor only sees types from rules that appear **before** the current rule. If you reference a type from a later rule, `GetConfig<T>()` throws because the type isn't loaded yet. The Roslyn analyzer **COCFG002** catches this at compile time.
:::

## Where It Works

The accessor is available in **every** provider factory overload and in `.When()`:

### Dynamic file paths

```csharp
rule.For<TenantConfig>().FromFile(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenants/{tenant.TenantId}.json");
})
```

### Dynamic HTTP endpoints

```csharp
rule.For<ApiConfig>().FromHttp(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return new HttpRuleOptions(
        $"https://{tenant.Region}.config.example.com/api",
        pollInterval: TimeSpan.FromMinutes(5));
})
```

### Dynamic environment prefixes

```csharp
rule.For<TenantConfig>().FromEnvironment(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return new EnvironmentVariableRuleOptions($"TENANT_{tenant.TenantId}_");
})
```

### Conditional execution

```csharp
rule.For<PremiumFeatures>().FromFile("premium.json")
    .When(accessor => accessor.GetConfig<TenantSettings>().IsPremium)
```

See [Conditional Rules](/guide/configuration/conditional-rules) for the full `.When()` documentation.

### Derived values

```csharp
rule.For<ComputedConfig>().FromStatic(accessor =>
{
    var app = accessor.GetConfig<AppSettings>();
    var db = accessor.GetConfig<DatabaseConfig>();
    return new ComputedConfig
    {
        ConnectionString = $"Server={db.Host};Database={app.AppName}_db"
    };
})
```

## Re-evaluation on Change

Config-aware factories are re-evaluated during every recompute. When the upstream config changes, the dependent rule sees the new values and adapts:

1. `tenant.json` changes — region goes from `us-east` to `eu-west`
2. Engine starts recompute, re-evaluates all rules in order
3. The HTTP polling rule's factory runs again, reads the new region
4. It returns a new URL → the provider switches to the EU endpoint
5. New config is fetched from the EU endpoint

This happens automatically. No manual wiring, no event handlers, no polling logic.

## Patterns

### Feature-gated sources

Load additional configuration only when a feature is enabled:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),

    rule.For<ExperimentalConfig>().FromHttp(accessor =>
    {
        var app = accessor.GetConfig<AppSettings>();
        return new HttpRuleOptions(
            app.ExperimentalEndpoint,
            pollInterval: TimeSpan.FromMinutes(10));
    })
    .When(accessor => accessor.GetConfig<AppSettings>().EnableExperiments),
]
```

The `.When()` and the factory both use the accessor. The rule only executes when the feature is enabled, and the URL comes from config.

### Multi-tenant overrides

Base config + tenant-specific overrides from different sources:

```csharp
rule => [
    rule.For<TenantSettings>().FromFile("tenant.json").Required(),

    rule.For<AppSettings>().FromFile("appsettings.json").Required(),

    rule.For<AppSettings>().FromHttp(accessor =>
    {
        var tenant = accessor.GetConfig<TenantSettings>();
        return new HttpRuleOptions(
            $"https://config.example.com/tenants/{tenant.TenantId}",
            pollInterval: TimeSpan.FromMinutes(5));
    })
    .When(accessor => accessor.GetConfig<TenantSettings>().HasCustomConfig),
]
```

### Safe access with TryGetConfig

When you're unsure whether a type has rules defined:

```csharp
rule.For<Overrides>().FromFile(accessor =>
{
    if (accessor.TryGetConfig<TenantSettings>(out var tenant))
        return FileSourceRuleOptions.FromFilePath($"overrides/{tenant.TenantId}.json");

    return FileSourceRuleOptions.FromFilePath("overrides/default.json");
})
```

## How It Differs from Conditional Rules

[Conditional Rules](/guide/configuration/conditional-rules) (`.When()`) are one application of the accessor — they decide **whether** a rule runs. Config-aware rules are the broader concept: the accessor can influence **what** to load, **where** to load it from, and **how** to configure the provider. `.When()` is a boolean gate; the accessor in factory overloads controls everything else.
