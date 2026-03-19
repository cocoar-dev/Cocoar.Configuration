# Flags Diagnostics & Source Generator

## COCFLAG001 — Non-Static ExpiresAt {#cocflag001}

**Severity:** Warning

`ExpiresAt` must be a static `DateTimeOffset` literal so the source generator can embed it in the generated descriptor. Dynamic expressions can't be evaluated at compile time.

```csharp
// ❌ Warning: ExpiresAt can't be determined at compile time
public partial class MyFlags : IFeatureFlags<MyConfig>
{
    public DateTimeOffset ExpiresAt => DateTimeOffset.UtcNow.AddMonths(6);
}
```

```csharp
// ✓ Compliant: Static literal
public partial class MyFlags : IFeatureFlags<MyConfig>
{
    public DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
}
```

If the generator can't determine `ExpiresAt`, it defaults to `DateTimeOffset.MinValue` — the class is treated as already expired and health reports `Degraded`.

## COCFLAG002 — Abstract Type Registered {#cocflag002}

**Severity:** Warning

`Register<T>()` was called with an abstract class. Abstract classes can't be instantiated as flag or entitlement instances.

```csharp
// ❌ Warning: Can't instantiate abstract class
public abstract partial class BaseFlags : IFeatureFlags<AppConfig> { }

.UseFeatureFlags(f => f.Register<BaseFlags>())
```

```csharp
// ✓ Fix: Register the concrete subclass
public partial class AppFlags : IFeatureFlags<AppConfig> { }

.UseFeatureFlags(f => f.Register<AppFlags>())
```

## COCFLAG003 — Missing Description {#cocflag003}

**Severity:** Info

A flag or entitlement property has no `<summary>` XML doc comment. Descriptions are surfaced through `IFeatureFlagsDescriptors` and `IEntitlementsDescriptors` — without them, operators see empty descriptions in tooling and the REST API.

```csharp
// ℹ️ Info: No description
public partial class AppFlags : IFeatureFlags<AppConfig>
{
    public FeatureFlag<bool> NewDashboard { get; }
}
```

```csharp
// ✓ Fix: Add a summary
public partial class AppFlags : IFeatureFlags<AppConfig>
{
    /// <summary>Enables the redesigned billing dashboard.</summary>
    public FeatureFlag<bool> NewDashboard { get; }
}
```

## Source Generator {#source-generator}

The source generator is a core part of the flags system — not optional. It produces the descriptor metadata that the runtime uses for health monitoring, REST endpoints, and the `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` APIs. It ships with the `Cocoar.Configuration` package and runs automatically at compile time.

### What It Generates

When you call `Register<T>()` in your `UseFeatureFlags()` or `UseEntitlements()` setup, the generator produces a `CocoarFlagsDescriptors` class containing:

- A dictionary of `FeatureFlagClassDescriptor` entries (one per flag class)
- A dictionary of `EntitlementClassDescriptor` entries (one per entitlement class)

Each descriptor includes:
- The class `Type`
- `ExpiresAt` (flags only)
- A list of property descriptors (name + description from XML doc)

### How It's Used

The generated descriptors are consumed internally by:

- **`IFeatureFlagsDescriptors`** — provides `All` and `Expired` collections for querying flag metadata
- **`IEntitlementsDescriptors`** — provides `All` collection for entitlement metadata
- **Health monitoring** — checks `Expired` to determine if health should report `Degraded`
- **REST endpoints** — uses descriptors to generate endpoint routes

You don't reference the generated class directly — it's wired up automatically during DI registration.

### Deterministic Output

The generator sorts all types alphabetically by full name, ensuring the same input always produces identical output. This prevents unnecessary rebuilds and diff noise in source control.
