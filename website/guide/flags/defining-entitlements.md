# Defining Entitlements

Entitlements are `partial class` types that implement `IEntitlements<TConfig>` and define entitlement properties as delegates. Unlike feature flags, entitlements have no expiration date — they represent permanent business logic.

## Basic Structure

```csharp
public partial class PlanEntitlements : IEntitlements<PlanConfig>
{
    /// <summary>Maximum allowed team members for this plan.</summary>
    public Entitlement<int> MaxUsers => () => Config.UserLimit;

    /// <summary>Whether this plan can export data.</summary>
    public Entitlement<bool> CanExport => () => Config.Tier != "free";

    /// <summary>Storage limit in gigabytes.</summary>
    public Entitlement<int> StorageLimitGb => () => Config.Tier switch
    {
        "enterprise" => 1000,
        "pro" => 100,
        _ => 5
    };
}
```

The class must be `partial` so the source generator can emit a constructor that accepts `IReactiveConfig<PlanConfig>`. The generated `Config` property returns `IReactiveConfig<T>.CurrentValue`, so it always reflects the latest configuration.

### Key Elements

| Element | Purpose |
|---|---|
| `IEntitlements<TConfig>` | Marks this as an entitlement class; source generator produces constructor and `Config` property |
| `partial class` | Required — the source generator emits the other half |
| `Config` | Source-generated property — reads `IReactiveConfig<TConfig>.CurrentValue` |
| `Entitlement<TResult>` | A no-context entitlement — returns a value based on current config |
| XML `<summary>` | Description extracted by the source generator for REST endpoints |

## Entitlement Types

### No-context entitlements

`Entitlement<TResult>` is a parameterless delegate:

```csharp
/// <summary>Whether API access is enabled.</summary>
public Entitlement<bool> ApiAccess => () => Config.Tier != "free";
```

### Contextual entitlements

`Entitlement<TContext, TResult>` takes a context parameter — for per-tenant or per-user decisions:

```csharp
/// <summary>Maximum API requests per minute for a specific tenant.</summary>
public Entitlement<TenantContext, int> RateLimit => tenant => tenant.Tier switch
{
    "enterprise" => 10000,
    "pro" => 1000,
    _ => 100
};
```

The `TContext` is resolved at evaluation time via a [Context Resolver](/guide/flags/context-resolvers).

## Multiple Config Sources <Badge type="info" text="ADV" />

Entitlements can combine multiple configuration types using a tuple:

```csharp
public partial class AccessEntitlements : IEntitlements<(PlanConfig, FeatureConfig)>
{
    /// <summary>Whether advanced analytics are available.</summary>
    public Entitlement<bool> AdvancedAnalytics => () =>
        Config.Item1.Tier == "enterprise" &&
        Config.Item2.AnalyticsEnabled;
}
```

The source generator injects `IReactiveConfig<(PlanConfig, FeatureConfig)>` and `Config` returns the combined tuple. When either config changes, the next entitlement invocation returns the updated result.

You can also use named tuple elements for readability:

```csharp
public partial class AccessEntitlements : IEntitlements<(PlanConfig Plan, FeatureConfig Features)>
{
    public Entitlement<bool> AdvancedAnalytics => () =>
        Config.Plan.Tier == "enterprise" &&
        Config.Features.AnalyticsEnabled;
}
```

## No ExpiresAt

The key difference from feature flags: entitlements have **no expiration**. They represent permanent product logic that doesn't need cleanup:

```csharp
// Feature flag — temporary, must expire
public partial class BetaFlags : IFeatureFlags<BetaConfig>
{
    public DateTimeOffset ExpiresAt => new(2026, 6, 1, ...);
}

// Entitlement — permanent, no expiry
public partial class PlanEntitlements : IEntitlements<PlanConfig>
{
    // No ExpiresAt needed
}
```

## Using Entitlements Directly

Inject the entitlement class and invoke properties:

```csharp
public class ExportService(PlanEntitlements entitlements)
{
    public async Task<ExportResult> ExportAsync(ExportRequest request)
    {
        if (!entitlements.CanExport())
            throw new ForbiddenException("Export not available on your plan");

        var limit = entitlements.StorageLimitGb();
        // ...
    }
}
```

Entitlement classes are **Singleton** — safe to inject anywhere.

