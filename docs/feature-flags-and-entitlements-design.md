# Cocoar.Configuration: Feature Flags & Entitlements Design

## Executive Summary

This document defines the design for Feature Flags and Entitlements in Cocoar.Configuration. The key insight: **Flags are not properties, they are decisions derived from configuration.**

---

## Part 1: Mental Model

### Core Principle

> **Feature Flags and Entitlements are technically similar, but conceptually different.**

Both are configuration-driven decisions evaluated at runtime. The separation exists **by design** to preserve clarity, ownership, and architectural health.

### The Fundamental Question

```
❌  "Is this enabled?"                    → Property thinking (wrong)
✅  "Is this enabled FOR THIS CONTEXT?"   → Decision thinking (correct)
```

A flag is not a value. It is a **question evaluated in context**.

### Feature Flags vs Entitlements

| Aspect | Feature Flag | Entitlement |
|--------|--------------|-------------|
| **Core Question** | "Does this code run?" | "May this actor do this?" |
| **Nature** | Technical / Operational | Business / Domain |
| **Lifetime** | **Temporary** (MUST have expiration) | **Permanent** (no expiration) |
| **Ownership** | Engineering / Ops | Product / Business |
| **Explains to** | DevOps | Product / Sales |

### The Litmus Test

> **"A Feature Flag without an expiration date is an Entitlement in disguise."**

### Expiration = Mental Reminder

Feature Flag expiration is **NOT a hard stop**:
- When expiration is reached → reported in logs and health API
- Flag **continues working**
- It's a code hygiene signal: "This temporary code should be cleaned up"

### Composition Rule (Optional)

Flags and Entitlements CAN be used together:

```
Capability Available = Feature Flag ENABLED  AND  Entitlement GRANTED
```

But this is not mandatory. They can be used independently.

---

## Part 2: Flags Derive FROM Config

### Key Insight

**No new providers needed!** Flags are a computation layer on top of existing Cocoar.Configuration:

```
┌─────────────────────────────────────────────────────────────┐
│         Existing Cocoar.Configuration System                │
│  (File, Environment, HTTP, CommandLine providers)           │
│                         ↓                                   │
│              ConfigManager (rules, layering)                │
│                         ↓                                   │
│         Concrete Config Types (strongly typed)              │
│         ┌──────────┐ ┌──────────┐ ┌──────────┐             │
│         │BillingCfg│ │TenantCfg │ │ PlanCfg  │             │
│         └──────────┘ └──────────┘ └──────────┘             │
└─────────────────────────────────────────────────────────────┘
                         ↓
                 FLAGS LAYER (NEW)
              (computed/derived from config)
                         ↓
┌─────────────────────────────────────────────────────────────┐
│  ┌──────────────────┐    ┌──────────────────┐              │
│  │FeatureFlags class│    │Entitlements class│              │
│  │  (auto-registers)│    │  (auto-registers)│              │
│  └──────────────────┘    └──────────────────┘              │
│                ↓                    ↓                       │
│  ┌──────────────────┐    ┌──────────────────┐              │
│  │FeatureFlagsReg.  │    │EntitlementsReg.  │              │
│  │   (catalog)      │    │   (catalog)      │              │
│  └──────────────────┘    └──────────────────┘              │
└─────────────────────────────────────────────────────────────┘
```

### Primitive vs Contextual

| Type | Context | API Style | Example |
|------|---------|-----------|---------|
| **Primitive** | None needed | Property access | `flags.Enabled` → `true` |
| **Primitive\<T\>** | None needed | Property access | `flags.Version` → `2` |
| **Contextual** | Required | Function call | `flags.EnabledForUser.Evaluate(ctx)` → `true` |

---

## Part 3: Dedicated Types

### Why Separate Types?

The **type name in code** communicates intent immediately:

```csharp
// Clear from the type what this is:
public FeatureFlag NewFlowEnabled => ...      // Temporary, technical
public Entitlement CanExport => ...           // Permanent, business rule
```

### FeatureFlag Types (for FeatureFlags classes)

| Type | Usage |
|------|-------|
| `FeatureFlag` | Simple on/off (bool) |
| `FeatureFlag<T>` | Typed value (int, string, enum) |
| `FeatureFlag<TContext, TResult>` | Contextual evaluation |

### Entitlement Types (for Entitlements classes)

| Type | Usage |
|------|-------|
| `Entitlement` | Simple on/off (bool) |
| `Entitlement<T>` | Typed value (int, string, enum) |
| `Entitlement<TContext, TResult>` | Contextual evaluation |

---

## Part 4: Base Classes

### FeatureFlags (temporary, MUST expire)

```csharp
public abstract class FeatureFlags
{
    protected FeatureFlags(IFeatureFlagsRegistry? registry = null)
    {
        registry?.Register(this);  // Auto-register for discovery
    }

    public abstract DateTimeOffset ExpiresAt { get; }
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
```

### Entitlements (permanent, no expiration)

```csharp
public abstract class Entitlements
{
    protected Entitlements(IEntitlementsRegistry? registry = null)
    {
        registry?.Register(this);  // Auto-register for discovery
    }
    // No ExpiresAt - these are permanent product logic
}
```

---

## Part 5: Value Types

### FeatureFlag (primitive bool)

```csharp
public readonly struct FeatureFlag
{
    private readonly bool _value;

    public FeatureFlag(bool value) => _value = value;
    public bool Value => _value;

    public static implicit operator FeatureFlag(bool value) => new(value);
    public static implicit operator bool(FeatureFlag flag) => flag._value;

    public override string ToString() => $"FeatureFlag({_value})";
}
```

### FeatureFlag\<T\> (primitive typed)

```csharp
public readonly struct FeatureFlag<T>
{
    private readonly T _value;

    public FeatureFlag(T value) => _value = value;
    public T Value => _value;

    public static implicit operator FeatureFlag<T>(T value) => new(value);
    public static implicit operator T(FeatureFlag<T> flag) => flag._value;

    public override string ToString() => $"FeatureFlag<{typeof(T).Name}>({_value})";
}
```

### FeatureFlag\<TContext, TResult\> (contextual)

```csharp
public readonly struct FeatureFlag<TContext, TResult>
{
    private readonly Func<TContext, TResult>? _evaluator;

    public FeatureFlag(Func<TContext, TResult> evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public TResult Evaluate(TContext context)
    {
        if (_evaluator is null)
            throw new InvalidOperationException("FeatureFlag evaluator not configured.");
        return _evaluator(context);
    }

    public TResult this[TContext context] => Evaluate(context);

    public static implicit operator FeatureFlag<TContext, TResult>(Func<TContext, TResult> func)
        => new(func);
}
```

> **Note:** Entitlement types follow the same pattern with `Entitlement`, `Entitlement<T>`, and `Entitlement<TContext, TResult>`.

---

## Part 6: Registries

### Purpose

Registries provide a **catalog of all flag/entitlement classes** in the application for:
- ConfigHub management UI
- Health checks (expired feature flags)
- Documentation generation

### IFeatureFlagsRegistry

```csharp
public interface IFeatureFlagsRegistry
{
    void Register(FeatureFlags featureFlags);
    bool Unregister(FeatureFlags featureFlags);
    IReadOnlyCollection<FeatureFlags> GetAll();
    T? Find<T>() where T : FeatureFlags;
    IReadOnlyCollection<FeatureFlags> GetExpired();
}
```

### IEntitlementsRegistry

```csharp
public interface IEntitlementsRegistry
{
    void Register(Entitlements entitlements);
    bool Unregister(Entitlements entitlements);
    IReadOnlyCollection<Entitlements> GetAll();
    T? Find<T>() where T : Entitlements;
}
```

---

## Part 7: The Pattern

### FeatureFlags Example

```csharp
public class BillingFeatureFlags : FeatureFlags
{
    public override DateTimeOffset ExpiresAt => new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IReactiveConfig<BillingConfig> _config;

    // Primitive flags - evaluated on access, always fresh
    public FeatureFlag NewFlowEnabled => _config.CurrentValue.NewFlowEnabled;
    public FeatureFlag<int> FlowVersion => _config.CurrentValue.FlowVersion;

    // Contextual flag - initialized once in constructor to avoid closure allocation
    public FeatureFlag<UserContext, bool> EnabledForUser { get; }

    public BillingFeatureFlags(
        IReactiveConfig<BillingConfig> config,
        IFeatureFlagsRegistry? registry = null) : base(registry)
    {
        _config = config;
        EnabledForUser = new(user =>
            _config.CurrentValue.NewFlowEnabled &&
            _config.CurrentValue.BetaUsers.Contains(user.Id));
    }
}
```

### Entitlements Example

```csharp
public class PlanEntitlements : Entitlements
{
    private readonly IReactiveConfig<PlanConfig> _config;

    // Primitive entitlements
    public Entitlement CanExport => _config.CurrentValue.Tier != "free";
    public Entitlement<int> MaxUsers => _config.CurrentValue.UserLimit;

    // Contextual entitlement - initialized once in constructor
    public Entitlement<TenantContext, bool> HasFeature { get; }

    public PlanEntitlements(
        IReactiveConfig<PlanConfig> config,
        IEntitlementsRegistry? registry = null) : base(registry)
    {
        _config = config;
        HasFeature = new(ctx =>
            _config.CurrentValue.EnabledFeatures.Contains(ctx.Feature));
    }
}
```

> **Best Practice:** Initialize contextual flags in the constructor to avoid closure allocation on every property access.

---

## Part 8: Registration & Usage

### DI Registration

```csharp
// Register registries as singletons
builder.Services.AddSingleton<IFeatureFlagsRegistry, FeatureFlagsRegistry>();
builder.Services.AddSingleton<IEntitlementsRegistry, EntitlementsRegistry>();

// Register flag classes as singletons (auto-registers with registry)
builder.Services.AddSingleton<BillingFeatureFlags>();
builder.Services.AddSingleton<PlanEntitlements>();
```

### Consumer Usage

```csharp
public class PaymentService
{
    private readonly BillingFeatureFlags _features;
    private readonly PlanEntitlements _entitlements;

    public PaymentService(BillingFeatureFlags features, PlanEntitlements entitlements)
    {
        _features = features;
        _entitlements = entitlements;
    }

    public void ProcessPayment(UserContext user)
    {
        // Primitive flag - just access like a bool
        if (_features.NewFlowEnabled)
        {
            // Contextual flag - pass context
            if (_features.EnabledForUser.Evaluate(user))
            {
                // New billing flow for this user
            }
        }

        // Entitlement check
        if (!_entitlements.CanExport)
        {
            throw new NotEntitledException("Export not available on free plan");
        }
    }
}
```

---

## Part 9: What We Get

| Feature | How |
|---------|-----|
| **Reactive** | `IReactiveConfig.CurrentValue` always returns fresh values |
| **Atomic** | Tuple ensures all configs from same snapshot |
| **Type-safe** | Dedicated types: `FeatureFlag` vs `Entitlement` |
| **Mental model** | Type name communicates intent |
| **Expiration tracking** | `IsExpired` property, `GetExpired()` on registry |
| **Discoverable** | Registries catalog all flag classes |
| **ConfigHub ready** | Registries provide inventory for management UI |
| **Testable** | Mock `IReactiveConfig`, inject, test |
| **Pure C#** | No magic, no attributes, IntelliSense works |

---

## Part 10: Future Enhancements

1. **Source Generator** - Auto-generate flag classes from expressions
2. **Health Integration** - Report expired FeatureFlags in health API
3. **API Endpoint** - Expose flags to clients with context evaluation
4. **Scanning** - `AddCocoarFlags(options => options.ScanAssembly(...))`
5. **Per-flag metadata** - Description, categories, individual expiration

---

## Summary

### The Pattern

1. Inherit from `FeatureFlags` (with `ExpiresAt`) or `Entitlements`
2. Pass registry to base constructor for auto-registration
3. Inject `IReactiveConfig<T>` for config access
4. Use `FeatureFlag`/`Entitlement` types for values
5. Initialize contextual flags in constructor (avoid closure allocation)
6. Register as Singleton in DI

### Key Files

| File | Purpose |
|------|---------|
| `FeatureFlags.cs` | Base class with `ExpiresAt` + auto-registration |
| `Entitlements.cs` | Base class + auto-registration |
| `FeatureFlag.cs` | `FeatureFlag` struct (primitive bool) |
| `FeatureFlagOfT.cs` | `FeatureFlag<T>` struct (primitive typed) |
| `FeatureFlagWithContext.cs` | `FeatureFlag<TContext, TResult>` struct |
| `Entitlement.cs` | `Entitlement` struct (primitive bool) |
| `EntitlementOfT.cs` | `Entitlement<T>` struct (primitive typed) |
| `EntitlementWithContext.cs` | `Entitlement<TContext, TResult>` struct |
| `IFeatureFlagsRegistry.cs` | Registry interface for FeatureFlags |
| `IEntitlementsRegistry.cs` | Registry interface for Entitlements |
| `FeatureFlagsRegistry.cs` | Thread-safe registry implementation |
| `EntitlementsRegistry.cs` | Thread-safe registry implementation |

---

*This design document serves as the foundation for implementing Feature Flags and Entitlements in Cocoar.Configuration.*
