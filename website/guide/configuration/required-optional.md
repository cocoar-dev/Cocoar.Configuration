# Required vs Optional Rules

Every rule is **optional by default**. This controls what happens when a provider fails — file not found, HTTP timeout, parse error.

## Optional Rules (Default)

When an optional rule fails, the system continues with graceful degradation:

```csharp
// Optional (default) — app continues if file is missing
rule.For<FeatureConfig>().FromFile("features.json")
```

**What happens on failure:**
- The rule contributes an empty JSON object `{}` — it adds nothing
- Values set by earlier rules remain unchanged
- If this is the only rule for the type, the object is created with C# default values
- Health status becomes `Degraded`
- The failure is tracked and visible in health monitoring
- The app keeps running

```csharp
public class FeatureConfig
{
    public bool EnableNewUI { get; set; } = false;  // Gets this default
    public int MaxItems { get; set; } = 10;          // Gets this default
}
```

## Required Rules

Mark a rule as required when the app cannot function without it:

```csharp
// Required — entire recompute rolls back if this fails
rule.For<DatabaseConfig>().FromFile("database.json").Required()
```

**What happens on failure:**

- **At startup:** Throws `ConfigurationDeserializationException` — the app does not start with broken config
- **At runtime (recompute):** The entire recompute is rolled back. All config types keep their previous values. Health status becomes `Unhealthy`.

The key insight: a required rule failure during recompute does not crash the app. It preserves the last known good state and signals the failure through health.

## Combining Required and Optional

A common pattern is a required base file with optional overrides:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Required()
        .Named("Base Config"),

    rule.For<AppSettings>().FromFile("appsettings.local.json")
        .Named("Local Overrides"),

    rule.For<AppSettings>().FromEnvironment("APP_"),
]
```

If `appsettings.json` is missing at startup, the app fails immediately — that's the correct behavior because the base configuration is essential. If `appsettings.local.json` is missing, it's silently skipped with defaults.

## Accessing Configuration

`GetConfig<T>()` returns the current configuration instance. It throws `InvalidOperationException` if no rule is registered for the type — this is a static check that catches missing registrations early:

```csharp
var config = manager.GetConfig<AppSettings>();
// Returns the config instance.
// Throws if no rules are defined for AppSettings.
```

For safe access when you're unsure if a type has rules:

```csharp
if (manager.TryGetConfig<AppSettings>(out var config))
{
    // config is available
}
```

In config-aware rules and `.When()` predicates, use `GetConfig<T>()` — you know the dependency exists because it was loaded by an earlier rule:

```csharp
rule.For<PremiumFeatures>().FromFile("premium.json")
    .When(accessor => accessor.GetConfig<TenantSettings>()!.IsPremium)
```

## Startup vs Runtime Behavior

| Scenario | Startup | Runtime Recompute |
|---|---|---|
| Required rule fails | App throws, does not start | Rolls back, keeps last good state |
| Optional rule fails | Continues with defaults | Continues with defaults |
| All rules succeed | Config loaded normally | New snapshot replaces old one |

This dual behavior means: strict validation at startup (catch misconfigurations early), resilient behavior at runtime (never lose working state because of a transient failure).
