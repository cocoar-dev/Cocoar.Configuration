# Feature Flags & Entitlements — Current State

This document describes what is implemented today in `Cocoar.Configuration.Flags`, how it works, and how to use it. It is a factual snapshot of the current code — not a design proposal.

---

## The Core Idea

Feature flags and entitlements are **not configuration properties**. They are **decisions derived from configuration**, evaluated at call time.

```
Existing Cocoar.Configuration (file, env, HTTP, ...)
                    ↓
         ConfigManager + rules
                    ↓
      Concrete config types (BillingConfig, PlanConfig, ...)
                    ↓
         IReactiveConfig<T>.CurrentValue       ← always fresh
                    ↓
    FeatureFlags / Entitlements classes         ← THIS LIBRARY
    (delegates that evaluate config on each call)
                    ↓
              Your application code
```

No new providers are needed. The flags library is a pure computation layer on top of what already exists.

---

## Feature Flags vs Entitlements

| | Feature Flag | Entitlement |
|---|---|---|
| **Question** | "Does this code run?" | "May this actor do this?" |
| **Nature** | Technical / operational | Business / domain |
| **Lifetime** | **Temporary** — MUST have an expiration | **Permanent** — no expiration |
| **Ownership** | Engineering / Ops | Product / Business |
| **Expiration effect** | Logged + health signal, flag **keeps working** | N/A |

**The litmus test:** A feature flag without an expiration date is an entitlement in disguise.

---

## Delegate Types

Four delegate types form the public API surface for consuming flags and entitlements.

```csharp
// Feature flags
public delegate TResult Flag<out TResult>();
public delegate TResult Flag<in TContext, out TResult>(TContext context);

// Entitlements
public delegate TResult Entitlement<out TResult>();
public delegate TResult Entitlement<in TContext, out TResult>(TContext context);
```

A `Flag<bool>` is called like a method: `flags.NewFlowEnabled()`. A `Flag<UserContext, bool>` is called with context: `flags.EnabledForUser(user)`. This is consistent for all types — even a simple bool is called, never accessed as a property.

---

## Base Classes

### `FeatureFlags`

```csharp
public abstract class FeatureFlags : IDisposable
{
    protected FeatureFlags(IFeatureFlagsRegistry? registry = null)

    // Required: when should this class be cleaned up from code?
    public abstract DateTimeOffset ExpiresAt { get; }

    // True when UtcNow > ExpiresAt. Flag still works — this is a hygiene signal.
    public bool IsExpired { get; }

    // Define a simple (non-contextual) flag
    protected Flag<T> DefineFlag<T>(
        string name,
        Func<T> valueFactory,
        DateTimeOffset? expiresAt = null,   // overrides ExpiresAt for this specific flag
        string? description = null)

    // Define a contextual flag
    protected Flag<TContext, TResult> DefineFlag<TContext, TResult>(
        string name,
        Func<TContext, TResult> evaluator,
        DateTimeOffset? expiresAt = null,
        string? description = null)

    // Metadata access
    public FeatureFlagMetadata? GetMetadata(Delegate flag)
    public IEnumerable<FeatureFlagMetadata> GetAllMetadata()
    public IEnumerable<FeatureFlagMetadata> GetExpiredFlags()

    public void Dispose()
}
```

### `Entitlements`

```csharp
public abstract class Entitlements : IDisposable
{
    protected Entitlements(IEntitlementsRegistry? registry = null)

    // No ExpiresAt — entitlements are permanent

    protected Entitlement<T> DefineEntitlement<T>(
        string name,
        Func<T> valueFactory,
        string? description = null)

    protected Entitlement<TContext, TResult> DefineEntitlement<TContext, TResult>(
        string name,
        Func<TContext, TResult> evaluator,
        string? description = null)

    public EntitlementMetadata? GetMetadata(Delegate entitlement)
    public IEnumerable<EntitlementMetadata> GetAllMetadata()

    public void Dispose()
}
```

---

## Metadata

Every flag and entitlement has metadata attached via the Capabilities system. It is retrieved by passing the delegate back to `GetMetadata`.

```csharp
// FeatureFlagMetadata
public sealed record FeatureFlagMetadata : IPrimaryCapability
{
    public required string Name { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? Description { get; init; }
    public bool IsExpired { get; }    // UtcNow > ExpiresAt
}

// EntitlementMetadata
public sealed record EntitlementMetadata : IPrimaryCapability
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    // No ExpiresAt
}
```

Metadata is stored in a `CapabilityScope` keyed by the delegate instance. The delegate itself is the lookup key, so `GetMetadata(flags.NewFlowEnabled)` retrieves exactly the metadata attached to that delegate.

---

## Writing a FeatureFlags Class

```csharp
public class BillingFeatureFlags : FeatureFlags
{
    // Required: when should this class be retired?
    public override DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IReactiveConfig<BillingConfig> _config;

    // Non-contextual flags: evaluated fresh on every call
    public Flag<bool> NewFlowEnabled { get; }
    public Flag<int> FlowVersion { get; }

    // Contextual flag: initialized once, closure-free, evaluated with context per call
    public Flag<UserContext, bool> EnabledForUser { get; }

    public BillingFeatureFlags(
        IReactiveConfig<BillingConfig> config,
        IFeatureFlagsRegistry? registry = null) : base(registry)
    {
        _config = config;

        NewFlowEnabled = DefineFlag(
            nameof(NewFlowEnabled),
            () => _config.CurrentValue.NewFlowEnabled,
            expiresAt: new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),  // per-flag override
            description: "Enables the new billing flow"
        );

        FlowVersion = DefineFlag(
            nameof(FlowVersion),
            () => _config.CurrentValue.FlowVersion,
            description: "Current billing flow version"
        );

        EnabledForUser = DefineFlag<UserContext, bool>(
            nameof(EnabledForUser),
            user => _config.CurrentValue.NewFlowEnabled &&
                    _config.CurrentValue.BetaUsers.Contains(user.Id),
            description: "Per-user beta access check"
        );
    }
}
```

**Key points:**
- Non-contextual flags are properties returning `Flag<T>` — the lambda inside re-reads `CurrentValue` on every call, so they are always fresh without any subscription.
- Contextual flags are initialized in the constructor to avoid allocating a new closure on every property access. The lambda captures `_config`, not a snapshot value.
- `expiresAt` on `DefineFlag` overrides the class-level `ExpiresAt` for that specific flag. Useful when individual flags within a class have different cleanup deadlines.

---

## Writing an Entitlements Class

```csharp
public class PlanEntitlements : Entitlements
{
    private readonly IReactiveConfig<PlanConfig> _config;

    public Entitlement<bool> CanExport { get; }
    public Entitlement<int> MaxUsers { get; }
    public Entitlement<TenantContext, bool> HasFeature { get; }

    public PlanEntitlements(
        IReactiveConfig<PlanConfig> config,
        IEntitlementsRegistry? registry = null) : base(registry)
    {
        _config = config;

        CanExport = DefineEntitlement(
            nameof(CanExport),
            () => _config.CurrentValue.Tier != "free",
            description: "Whether the plan allows data export"
        );

        MaxUsers = DefineEntitlement(
            nameof(MaxUsers),
            () => _config.CurrentValue.UserLimit,
            description: "Maximum allowed users on this plan"
        );

        HasFeature = DefineEntitlement<TenantContext, bool>(
            nameof(HasFeature),
            ctx => _config.CurrentValue.EnabledFeatures.Contains(ctx.Feature),
            description: "Whether a specific feature is enabled for this tenant"
        );
    }
}
```

Same structure as `FeatureFlags`, without `ExpiresAt`.

---

## Consuming Flags and Entitlements

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
        // Simple flag — called like a method, returns bool
        if (_features.NewFlowEnabled())
        {
            // Contextual flag — pass context as argument
            if (_features.EnabledForUser(user))
            {
                UseNewBillingFlow();
            }
        }

        // Typed flag — returns int
        int version = _features.FlowVersion();

        // Entitlement — same call style
        if (!_entitlements.CanExport())
            throw new NotEntitledException("Export not available on this plan");

        // Composition: flag AND entitlement
        bool canUseAdvancedExport = _features.NewFlowEnabled() && _entitlements.CanExport();

        // Contextual entitlement — pass context
        bool tenantHasApi = _entitlements.HasFeature(new TenantContext { Feature = "api-access" });
    }
}
```

Every call re-evaluates the underlying lambda, which re-reads `IReactiveConfig<T>.CurrentValue`. There is no caching, no subscription, no stale state. If the config changes between two calls, the second call returns the new value.

---

## Reactivity

Flags do not subscribe to config changes. They re-read `CurrentValue` on every invocation. This means:

- **No setup required** — works out of the box with `IReactiveConfig<T>`
- **Always fresh** — reflects the most recent config after any recompute
- **Atomic when using tuple configs** — `IReactiveConfig<(BillingConfig, PlanConfig)>` ensures both configs are from the same snapshot

```csharp
// Atomically consistent across multiple config types
public class BillingFeatureFlags : FeatureFlags
{
    private readonly IReactiveConfig<(BillingConfig Billing, PlanConfig Plan)> _config;

    public Flag<bool> PremiumFlowEnabled { get; }

    public BillingFeatureFlags(
        IReactiveConfig<(BillingConfig, PlanConfig)> config,
        IFeatureFlagsRegistry? registry = null) : base(registry)
    {
        _config = config;

        PremiumFlowEnabled = DefineFlag(
            nameof(PremiumFlowEnabled),
            () => _config.CurrentValue.Billing.NewFlowEnabled &&
                  _config.CurrentValue.Plan.Tier == "premium",
            description: "New flow for premium plans only"
        );
    }
}
```

---

## Registries

Registries are optional catalogs of all flag/entitlement class instances. They enable health checks, tooling, and management UIs to discover what exists in the application.

### Auto-registration

When a registry is injected into the base class constructor, the instance registers itself immediately:

```csharp
// This registers BillingFeatureFlags with the registry on construction
var flags = new BillingFeatureFlags(config, registry);

// Without a registry — works fine, just not discoverable
var flags = new BillingFeatureFlags(config);
```

### IFeatureFlagsRegistry

```csharp
public interface IFeatureFlagsRegistry
{
    void Register(FeatureFlags featureFlags);
    bool Unregister(FeatureFlags featureFlags);
    IReadOnlyCollection<FeatureFlags> GetAll();
    T? Find<T>() where T : FeatureFlags;
    IReadOnlyCollection<FeatureFlags> GetExpired();   // uses IsExpired on each instance
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
    // No GetExpired — entitlements don't expire
}
```

Both registries have thread-safe implementations (`FeatureFlagsRegistry`, `EntitlementsRegistry`) backed by `ConcurrentDictionary<Type, T>`. Registering the same type twice replaces the previous instance.

---

## DI Registration

Use `UseFeatureFlags` and `UseEntitlements` on the `ConfigManagerBuilder`. They wire up DI registration, singleton registry instances, and health monitoring in one call.

```csharp
// In Program.cs
builder.Services.AddCocoarConfiguration(c => c
    .UseConfiguration(
        rules => [
            rules.For<BillingConfig>().FromFile("billing.json"),
            rules.For<PlanConfig>().FromFile("plans.json")
        ],
        setup => [
            setup.ConcreteType<BillingConfig>(),
            setup.ConcreteType<PlanConfig>()
        ])
    .UseFeatureFlags(flags => flags
        .Register<BillingFeatureFlags>()
        .Register<ShippingFeatureFlags>())
    .UseEntitlements(e => e
        .Register<PlanEntitlements>()));
```

This registers as singletons:
- `IFeatureFlagsRegistry` → pre-created `FeatureFlagsRegistry` instance
- `IEntitlementsRegistry` → pre-created `EntitlementsRegistry` instance
- `BillingFeatureFlags`, `ShippingFeatureFlags` → resolved lazily, auto-register in the registry on first resolution
- `PlanEntitlements` → same

Because flag classes are singletons and the registry is injected through the constructor, auto-registration happens exactly once when the DI container first resolves the class.

`UseFeatureFlags` and `UseEntitlements` are independent — call either or both. Order does not matter.

---

## Testing

Flags are straightforward to test: mock `IReactiveConfig<T>` with NSubstitute (or any mock library) and pass it directly to the constructor. No `CocoarTestConfiguration` setup is required.

```csharp
public class BillingFeatureFlagsTests : IDisposable
{
    private readonly IReactiveConfig<BillingConfig> _config;
    private readonly BillingFeatureFlags _flags;

    public BillingFeatureFlagsTests()
    {
        _config = Substitute.For<IReactiveConfig<BillingConfig>>();
        _flags = new BillingFeatureFlags(_config);  // no registry needed in unit tests
    }

    [Fact]
    public void NewFlowEnabled_WhenConfigTrue_ReturnsTrue()
    {
        _config.CurrentValue.Returns(new BillingConfig { NewFlowEnabled = true });

        Assert.True(_flags.NewFlowEnabled());
    }

    [Fact]
    public void NewFlowEnabled_WhenConfigChanges_ReflectsNewValue()
    {
        var current = new BillingConfig { NewFlowEnabled = false };
        _config.CurrentValue.Returns(_ => current);

        Assert.False(_flags.NewFlowEnabled());

        current = new BillingConfig { NewFlowEnabled = true };

        Assert.True(_flags.NewFlowEnabled());
    }

    [Fact]
    public void EnabledForUser_OnlyMatchesBetaUsers()
    {
        _config.CurrentValue.Returns(new BillingConfig
        {
            NewFlowEnabled = true,
            BetaUsers = ["alice"]
        });

        Assert.True(_flags.EnabledForUser(new UserContext { Id = "alice" }));
        Assert.False(_flags.EnabledForUser(new UserContext { Id = "bob" }));
    }

    [Fact]
    public void GetMetadata_ReturnsCorrectExpiration()
    {
        var meta = _flags.GetMetadata(_flags.NewFlowEnabled);

        Assert.Equal("NewFlowEnabled", meta!.Name);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), meta.ExpiresAt);
    }

    public void Dispose() => _flags.Dispose();
}
```

**Mutable config in tests:** Use `Returns(_ => current)` (lambda overload) instead of `Returns(current)` (value overload) when the test mutates the config object between assertions. The lambda is re-evaluated on each access, mirroring real reactive behavior.

---

## Health API Integration

Expired feature flags classes appear in the health snapshot via `IConfigurationHealthService`. When any registered `FeatureFlags` class has passed its `ExpiresAt` date, the overall health status is `Degraded` (not `Unhealthy` — the flags keep working).

```csharp
var health = configManager.GetHealthService();
var snapshot = health.Snapshot;

// ExpiredFeatureFlags is populated after the first recompute following DI resolution
foreach (var entry in snapshot.ExpiredFeatureFlags)
{
    Console.WriteLine($"{entry.TypeName} expired {entry.ExpiresAt:d} — {entry.ExpiredFlags}/{entry.TotalFlags} flags past deadline");
}

// HealthStatus.Degraded when expired flags are present
Console.WriteLine(snapshot.OverallStatus);
```

**Timing:** The registry is populated lazily — when the DI container first resolves the flag class. The health snapshot is updated on every configuration recompute (file change, poll interval, etc.). If a flag class is resolved between recomputes, the next recompute will include it in the snapshot.

---

## Package Dependencies

`Cocoar.Configuration.Flags` depends on:

| Package | Why |
|---------|-----|
| `Cocoar.Configuration.Abstractions` | `IReactiveConfig<T>` interface |
| `Cocoar.Configuration` | `ConfigManagerBuilder` extension hooks, `IFlagsHealthSource` for health integration |
| `Cocoar.Capabilities` | Attaching `FeatureFlagMetadata` / `EntitlementMetadata` to delegate instances via `CapabilityScope` |

---

## What Is Not There Yet

| Area | Status |
|------|--------|
| ASP.NET Core endpoint | Not implemented — no `/flags` or `/entitlements` HTTP endpoint |
| Assembly scanning | Not implemented — no auto-discovery of `FeatureFlags`/`Entitlements` subclasses |
| Source generator | Not implemented |
