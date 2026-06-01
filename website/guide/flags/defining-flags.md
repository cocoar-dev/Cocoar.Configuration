---
description: Defining IFeatureFlags<TConfig> partial classes, FeatureFlag<TResult> and FeatureFlag<TContext,TResult> delegates, tuple multi-config, ExpiresAt, source-generated Config property
---

# Defining Feature Flags

Feature flags are `partial class` types that implement `IFeatureFlags<TConfig>` and define flag properties as delegates. The source generator produces the constructor and `Config` property automatically.

## Basic Structure

```csharp
public partial class AppFeatureFlags : IFeatureFlags<AppSettings>
{
    // Required: when should these flags be cleaned up?
    public DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Enables the new onboarding flow.</summary>
    public FeatureFlag<bool> NewOnboarding => () => Config.EnableNewOnboarding;

    /// <summary>Maximum items shown in the new list view.</summary>
    public FeatureFlag<int> NewListViewMaxItems => () => Config.ListViewMax;
}
```

The class must be `partial` so the source generator can emit a constructor that accepts `IReactiveConfig<AppSettings>`. The generated `Config` property returns `IReactiveConfig<T>.CurrentValue`, so it always reflects the latest configuration.

### Key Elements

| Element | Purpose |
|---|---|
| `IFeatureFlags<TConfig>` | Marks this as a feature flag class; source generator produces constructor and `Config` property |
| `partial class` | Required — the source generator emits the other half |
| `ExpiresAt` | Class-level expiration date — when should these flags be removed? |
| `Config` | Source-generated property — reads `IReactiveConfig<TConfig>.CurrentValue` |
| `FeatureFlag<TResult>` | A no-context flag — returns a value based on current config |
| XML `<summary>` | Description extracted by the source generator for health/REST endpoints |

## Flag Types

### No-context flags

`FeatureFlag<TResult>` is a parameterless delegate. It reads from `Config` and returns a result:

```csharp
/// <summary>Enables dark mode for all users.</summary>
public FeatureFlag<bool> DarkMode => () => Config.DarkModeEnabled;
```

### Contextual flags

`FeatureFlag<TContext, TResult>` takes a context parameter — for decisions that depend on the current user, tenant, or request:

```csharp
/// <summary>Gates the beta feature for specific users.</summary>
public FeatureFlag<UserContext, bool> BetaFeature => user => Config.BetaEnabled && user.IsBeta;
```

The `TContext` is resolved at evaluation time via a [Context Resolver](/guide/flags/context-resolvers).

## Multiple Config Sources <Badge type="info" text="ADV" />

A flag class can depend on multiple configuration types using a tuple:

```csharp
public partial class RolloutFlags : IFeatureFlags<(FeatureConfig, TenantConfig)>
{
    public DateTimeOffset ExpiresAt => new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Enables new checkout when both feature and tenant allow it.</summary>
    public FeatureFlag<bool> NewCheckout => () =>
        Config.Item1.NewCheckoutEnabled &&
        Config.Item2.AllowExperiments;
}
```

The source generator injects `IReactiveConfig<(FeatureConfig, TenantConfig)>` and `Config` returns the combined tuple. When either config changes, the next flag invocation returns the updated result.

You can also use named tuple elements for readability:

```csharp
public partial class RolloutFlags : IFeatureFlags<(FeatureConfig Features, TenantConfig Tenant)>
{
    public DateTimeOffset ExpiresAt => new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public FeatureFlag<bool> NewCheckout => () =>
        Config.Features.NewCheckoutEnabled &&
        Config.Tenant.AllowExperiments;
}
```

## Return Types <Badge type="info" text="ADV" />

Flags can return any type, not just booleans:

```csharp
/// <summary>Which checkout variant to show (A/B test).</summary>
public FeatureFlag<string> CheckoutVariant => () => Config.CheckoutVariant;

/// <summary>Rate limit for the new API (requests per minute).</summary>
public FeatureFlag<int> NewApiRateLimit => () => Config.NewApiRpm;

/// <summary>Full feature configuration for the experiment.</summary>
public FeatureFlag<ExperimentConfig> ExperimentSettings => () => Config.Experiment;
```

## ExpiresAt

Every feature flag class must declare when its flags should be cleaned up:

```csharp
public DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
```

This is a **class-level** expiration. A flag class groups flags belonging to one feature — the expiry date applies to the entire feature, not individual flags. When the feature is fully rolled out, the whole class should be removed.

After the expiry date:
- Flags continue to work normally
- The health API reports `Degraded`
- This signals to the team that the feature rollout is complete and the flags should be cleaned up

The source generator validates that `ExpiresAt` returns a static value. Dynamic expressions are rejected at compile time.

## Using Flags Directly

Inject the flag class and invoke properties directly:

```csharp
public class CheckoutService(AppFeatureFlags flags)
{
    public async Task<CheckoutResult> ProcessAsync(Order order)
    {
        if (flags.NewCheckout())
            return await NewCheckoutFlow(order);

        return await LegacyCheckoutFlow(order);
    }
}
```

Flag classes are **Singleton** — safe to inject anywhere. The delegate reads from `Config` (backed by `IReactiveConfig<T>.CurrentValue`), which always reflects the latest configuration.

:::warning Namespace collision with Config property
The source generator creates a `Config` property on your class. If your namespace contains `.Config` (e.g., `MyApp.Config`), the compiler may confuse the namespace with the property. Avoid naming namespaces `Config` when using `IFeatureFlags<T>` or `IEntitlements<T>`.
:::

