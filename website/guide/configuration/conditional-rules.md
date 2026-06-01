---
description: Conditionally enable rules with .When(accessor) over earlier config state, Skipped health status, dynamic source selection, COCFG002 rule-order checking
---

# Conditional Rules

Rules can be conditionally enabled based on the current configuration state. This is one of the most powerful features in Cocoar.Configuration — rules can depend on the results of earlier rules.

## Basic Conditional Rules

Use `.When()` to conditionally include a rule:

```csharp
rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    rule.For<PremiumFeatures>().FromFile("premium.json")
        .When(accessor => accessor.GetConfig<TenantSettings>().IsPremium),
]
```

When the predicate returns `false`, the rule is skipped entirely — no provider call, no merge, no deserialization. Health monitoring reports the rule as `Skipped`.

## How It Works

The `.When()` callback receives an `IConfigurationAccessor` — the same interface used to read config at runtime. At rule evaluation time, it reflects the state from all **earlier** rules that have already executed.

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),       // Rule 1 — always runs
    rule.For<TenantSettings>().FromFile("tenant.json"),         // Rule 2 — always runs

    rule.For<PremiumFeatures>().FromFile("premium.json")
        .When(a => a.GetConfig<TenantSettings>().Tier == "Premium"),
        // ↑ Can access AppSettings and TenantSettings from rules 1-2

    rule.For<AdvancedFeatures>().FromFile("advanced.json")
        .When(a => a.GetConfig<AppSettings>().EnableAdvanced),
        // ↑ Can access AppSettings, TenantSettings, and PremiumFeatures
]
```

::: warning Rule Order Matters
A `.When()` predicate can only read config types from rules that appear **before** it in the list. If you reference a type from a later rule, `GetConfig<T>()` will throw because the type isn't loaded yet. The Roslyn analyzer **COCFG002** catches this at compile time.
:::

## Dynamic Configuration

Conditional rules enable dynamic scenarios where the source itself depends on configuration:

```csharp
rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    // Load config from a region-specific endpoint
    rule.For<ApiSettings>().FromHttp(accessor =>
    {
        var tenant = accessor.GetConfig<TenantSettings>();
        return new HttpRuleOptions(
            $"https://{tenant.Region}.api.example.com/config",
            pollInterval: TimeSpan.FromMinutes(5));
    }),
]
```

The HTTP endpoint URL is derived from `TenantSettings.Region`. When the tenant file changes and the region changes, the HTTP polling rule automatically switches to the new endpoint.

## Re-evaluation on Change

Conditional predicates are re-evaluated during every recompute. If a previously-false condition becomes true (e.g., tenant upgrades to Premium), the rule activates and its provider starts contributing data. If a previously-true condition becomes false, the rule is skipped and its contribution is removed.

This means `.When()` is not a one-time check — it's a live decision that adapts as configuration changes.

## Common Patterns

### Environment-based rules

```csharp
rule.For<DebugSettings>().FromFile("debug.json")
    .When(_ => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
```

### Feature-gated configuration

```csharp
rule.For<ExperimentalConfig>().FromFile("experimental.json")
    .When(a => a.GetConfig<AppSettings>().EnableExperiments)
```

### Multi-tenant overrides

```csharp
rule.For<AppSettings>().FromHttp(a =>
{
    var tenant = a.GetConfig<TenantSettings>();
    return new HttpRuleOptions($"https://config.example.com/{tenant.TenantId}",
        pollInterval: TimeSpan.FromMinutes(5));
})
.When(a => a.GetConfig<TenantSettings>().HasCustomConfig)
```
