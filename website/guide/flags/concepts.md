# Feature Flags vs Entitlements

Cocoar.Configuration has built-in support for two related but distinct concepts: **feature flags** and **entitlements**.

## What They Are

Feature flags and entitlements are **computed values**, not stored values. They don't exist as their own entries in a config file — they are derived from configuration at runtime.

A flag or entitlement is a function that reads one or more configuration values and returns a result. Sometimes this is a simple pass-through (a single boolean from config), sometimes it's a computation combining multiple config sources:

```csharp
// Simple: passes through a single config value
public FeatureFlag<bool> NewDashboard => () => Config.UseNewDashboard;

// Computed: combines multiple values into a decision
public FeatureFlag<UserContext, bool> BetaCheckout => user =>
    Config.BetaEnabled && Config.BetaRegions.Contains(user.Region);
```

The key insight: **you cannot set a flag directly**. You change the underlying configuration values, and the flag recomputes. This keeps the config layer as the single source of truth.

## Feature Flags

Feature flags answer: **"Does this code run?"**

They represent temporary, operational toggles — rollouts, A/B tests, kill switches. They are owned by engineering/ops and must have an explicit expiration date.

```csharp
public partial class BillingFlags : IFeatureFlags<BillingConfig>
{
    public override DateTimeOffset ExpiresAt => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Enables the redesigned billing dashboard.</summary>
    public FeatureFlag<bool> NewDashboard => () => Config.UseNewDashboard;

    /// <summary>Gates beta checkout for specific users.</summary>
    public FeatureFlag<UserContext, bool> BetaCheckout => user => Config.BetaEnabled && user.IsBeta;
}
```

The `partial class` implements `IFeatureFlags<TConfig>`, and the source generator produces the constructor and `Config` property. `Config` reads `IReactiveConfig<TConfig>.CurrentValue`, so it always reflects the latest configuration.

After the expiration date, the flags **keep working** — but the health API reports `Degraded`, signaling that cleanup is overdue.

## Entitlements

Entitlements answer: **"May this actor do this?"**

They represent permanent business logic — plan tiers, feature availability, permission limits. They are owned by product/business and have **no expiration date**.

```csharp
public partial class PlanEntitlements : IEntitlements<PlanConfig>
{
    /// <summary>Maximum allowed team members.</summary>
    public Entitlement<int> MaxUsers => () => Config.UserLimit;

    /// <summary>Whether this plan can export data.</summary>
    public Entitlement<bool> CanExport => () => Config.Tier != "free";
}
```

## The Litmus Test

> A feature flag without an expiration date is an entitlement in disguise.

If you're unsure which to use, ask: will this toggle ever be removed? If yes, it's a feature flag. If it's permanent product logic, it's an entitlement.

## How They Work

Both are **pure functions over configuration state**:

1. Each property is a delegate that reads from `Config` (the source-generated property backed by `IReactiveConfig<T>.CurrentValue`)
2. When configuration changes, the next invocation returns the new result
3. No per-request state, no caching — just a function call
4. No constructor needed — the source generator handles wiring

## Why Delegates?

Flag and entitlement properties use delegate types (`FeatureFlag<T>`, `Entitlement<T>`) instead of plain properties or methods. This is a deliberate architectural constraint:

**Enforced simplicity.** A `FeatureFlag<TResult>` takes zero parameters. A `FeatureFlag<TContext, TResult>` takes exactly one. You cannot accidentally add extra parameters — the type system prevents it.

**REST API compatibility.** The REST evaluation pipeline maps directly to delegates:
- `FeatureFlag<TResult>` → `GET /flags/{Class}/{Property}` — no input needed
- `FeatureFlag<TContext, TResult>` → `POST /flags/{Class}/{Property}` — request body is deserialized, passed through a [Context Resolver](/guide/flags/context-resolvers), and the resolved context becomes the single delegate argument

**Pure functions.** Flags must not inject services or call databases. All they receive is configuration (via `Config`) and optionally a context object. This keeps evaluation fast, deterministic, and debuggable — no hidden I/O, no async, no side effects.

:::warning Flags are pure — side effects belong in resolvers
If a flag needs data from a database or external service, that logic belongs in a [Context Resolver](/guide/flags/context-resolvers). The resolver hydrates a rich context object; the flag makes a decision based on it.

```csharp
// ✗ WRONG — DB call in a flag
BetaCheckout = async user => await db.Users.IsBeta(user.Id);

// ✓ RIGHT — Resolver fetches data, flag decides
// Resolver:
public async Task<UserContext> ResolveAsync(UserIdRequest req)
    => new UserContext(await db.Users.FindAsync(req.UserId));

// Flag: pure function
BetaCheckout = user => Config.BetaEnabled && user.IsBeta;
```
:::

## With Context

Both support contextual evaluation — flags and entitlements that depend on runtime context (current user, tenant, request):

```csharp
/// <summary>Gates beta checkout for specific users.</summary>
public FeatureFlag<UserContext, bool> BetaCheckout => user => Config.BetaEnabled && user.IsBeta;
```

The context is resolved via [Context Resolvers](/guide/flags/context-resolvers) — a bridge between HTTP request data and your domain model.

## When to Use Cocoar Flags vs. Dedicated Flag Services

Cocoar flags and dedicated feature flag services (LaunchDarkly, Unleash, Flagsmith, etc.) solve different problems. Understanding the trade-offs helps you pick the right tool — or use both.

**Cocoar flags are a good fit when:**

- Flags are **derived from your own configuration** — plan tiers, tenant settings, deployment environment. The flag is a pure function over config you already manage.
- You need **tight integration with your config layer** — flags recompute automatically when configuration changes, share the same health monitoring, and follow the same lifecycle.
- You're an **ISV controlling feature rollout across your own instances** — each instance has its own config, and flags reflect that instance's state.
- You want **type-safe, compile-time validated flags** — the source generator catches missing flags, wrong return types, and expired flags at build time.

**Dedicated flag services are a better fit when:**

- You need a **management UI for non-developers** — product managers toggling flags without code changes or deployments.
- You need **percentage rollouts, A/B testing, or experimentation infrastructure** — statistical analysis, cohort management, and gradual rollout are their core competency.
- You need **cross-platform flag evaluation** — the same flag evaluated consistently across mobile apps, web frontends, and backend services with server-side SDKs.

**They can coexist.** Use Cocoar flags for config-derived decisions (plan limits, tenant features, deployment-specific toggles) and a dedicated service for user-targeting and experimentation. They solve different problems and don't conflict — a Cocoar entitlement might gate whether a feature is available on a plan, while a LaunchDarkly flag controls whether that feature's new UI is shown to 10% of users.

## Comparison

| | Feature Flags | Entitlements |
|---|---|---|
| Purpose | Temporary operational toggles | Permanent business logic |
| Expiry | Required (`ExpiresAt`) | None |
| Health signal | `Degraded` when expired | None |
| Interface | `IFeatureFlags<TConfig>` | `IEntitlements<TConfig>` |
| Property type | `FeatureFlag<TResult>` or `FeatureFlag<TContext, TResult>` | `Entitlement<TResult>` or `Entitlement<TContext, TResult>` |
| Owned by | Engineering / Ops | Product / Business |
| Lifecycle | Create → roll out → expire → remove | Create → keep forever |
