# Expiry & Health

Feature flags have a built-in lifecycle: they are created, deployed, and eventually removed. The expiry system tracks this lifecycle and signals when cleanup is overdue.

## How Expiry Works

Every feature flags class declares an expiration date:

```csharp
public partial class BillingFlags : IFeatureFlags<BillingConfig>
{
    public override DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    // ...
}
```

When `DateTimeOffset.UtcNow` passes `ExpiresAt`:
- The flags **keep working** — no behavior change
- The health API reports `Degraded`
- `IFeatureFlagsDescriptors.Expired` includes the class

This is a cleanup signal, not a kill switch. Expired flags don't stop functioning — they just remind you to remove the code.

## Checking Expiry

### Via IFeatureFlagsDescriptors

```csharp
public class CleanupService(IFeatureFlagsDescriptors descriptors)
{
    public void CheckExpired()
    {
        foreach (var expired in descriptors.Expired)
        {
            Console.WriteLine($"{expired.Type.Name} expired at {expired.ExpiresAt}");
            foreach (var flag in expired.Flags)
            {
                Console.WriteLine($"  - {flag.Name}: {flag.Description}");
            }
        }
    }
}
```

### Via Descriptors

```csharp
// All registered flag classes
IReadOnlyList<FeatureFlagClassDescriptor> all = descriptors.All;

// Only expired classes
IReadOnlyList<FeatureFlagClassDescriptor> expired = descriptors.Expired;

// Per-class check
bool isExpired = descriptor.IsExpired;
```

## Health Integration

The flags system contributes to the overall health status:

| Condition | Health Status |
|---|---|
| All flags within expiry | `Healthy` |
| One or more flag classes expired | `Degraded` |

This integrates with the standard [Health Monitoring](/guide/health/overview) system. In ASP.NET Core, expired flags show up in the health endpoint response.

## Entitlements Have No Expiry

Entitlements are permanent business logic — they don't expire. `IEntitlementsDescriptors` has an `All` property but no `Expired` property:

```csharp
public interface IEntitlementsDescriptors
{
    IReadOnlyList<EntitlementClassDescriptor> All { get; }
    // No Expired — entitlements don't expire
}
```

## Source Generator Validation

The source generator validates `ExpiresAt` at compile time:

- The return value must be a static `DateTimeOffset` literal
- Dynamic expressions (e.g., `DateTimeOffset.UtcNow.AddMonths(6)`) emit a diagnostic error
- This ensures the expiry date is deterministic and visible in generated descriptors

## Lifecycle

The intended lifecycle for feature flags:

1. **Define** — create the flag class with an `ExpiresAt` in the near future
2. **Roll out** — gradually enable the feature via config changes
3. **Stabilize** — once fully rolled out, the flag always returns `true`
4. **Expire** — `ExpiresAt` passes, health reports `Degraded`
5. **Clean up** — remove the flag class, inline the behavior, delete the config

If a flag is still needed after expiry, either extend the date (conscious decision) or convert it to an entitlement (permanent business logic).
