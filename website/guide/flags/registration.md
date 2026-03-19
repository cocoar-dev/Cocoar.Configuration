# Registration

Feature flags and entitlements are registered on the `ConfigManagerBuilder` using `UseFeatureFlags()` and `UseEntitlements()`.

## Basic Registration

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
        rule.For<PlanConfig>().FromFile("plan.json"),
    ])
    .UseFeatureFlags(flags => [
        flags.Register<AppFeatureFlags>(),
        flags.Register<BillingFlags>()
    ])
    .UseEntitlements(entitlements => [
        entitlements.Register<PlanEntitlements>()
    ]));
```

Each `Register<T>()` call adds a flag or entitlement class to the system. The collection expression syntax (`[]`) matches `UseConfiguration`.

## What Registration Does

1. Registers the class as **Singleton** in DI
2. Uses the source generator to extract flag/entitlement descriptors (names, descriptions, expiry)
3. Registers `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` for health and REST endpoints
4. Pre-compiles evaluation delegates for the REST API

## With Context Resolvers <Badge type="info" text="ADV" />

When flags or entitlements have contextual properties (`FeatureFlag<TContext, TResult>`), you need to register resolvers that bridge HTTP requests to your domain context.

Resolver registration lives in the DI package (`Cocoar.Configuration.DI` or `Cocoar.Configuration.AspNetCore`). It appears as a second parameter on `UseFeatureFlags()` / `UseEntitlements()`:

```csharp
.UseFeatureFlags(
    flags => [
        flags.Register<AppFeatureFlags>(),
        flags.Register<BillingFlags>()
    ],
    resolvers => [
        resolvers.Global<UserByIdResolver>(),
        resolvers.For<BillingFlags>(r => r
            .Use<BillingResolver>()
            .ForProperty(f => f.BetaCheckout).Use<BetaResolver>())
    ])
```

Resolvers can be registered at three levels:

### Global (lowest priority)

Applies to all flag/entitlement properties with a matching `TContext`:

```csharp
resolvers.Global<UserByIdResolver>()
```

### Class-level

Applies to all properties in one class:

```csharp
resolvers.For<AppFeatureFlags>(r => r
    .Use<UserByIdResolver>())
```

### Property-level (highest priority)

Applies to one specific property:

```csharp
resolvers.For<AppFeatureFlags>(r => r
    .ForProperty(f => f.BetaByEmail).Use<UserByEmailResolver>())
```

### Priority Cascade

When evaluating a contextual flag, the system looks for a resolver in this order:

1. **Property-level** — if registered for this specific property, use it
2. **Class-level** — if registered for this class, use it
3. **Global** — if registered globally for this `TContext`, use it

See [Context Resolvers](/guide/flags/context-resolvers) for full details.

## Without DI

Without DI, the Core-only overload takes a single parameter (no resolver registration):

```csharp
using var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
    ])
    .UseFeatureFlags(flags => [
        flags.Register<AppFeatureFlags>()
    ]));

var flags = manager.GetFeatureFlags<AppFeatureFlags>();
var enabled = flags.NewOnboarding();
```

## Source Generator

The source generator that produces descriptor metadata ships with the `Cocoar.Configuration` package — no separate install needed. It runs automatically at compile time when you reference `Cocoar.Configuration`.

For `IFeatureFlags<TConfig>` and `IEntitlements<TConfig>` classes, the source generator also produces:
- A constructor that accepts `IReactiveConfig<TConfig>` (or `IReactiveConfig<(T1, T2)>` for tuples)
- The `Config` property that returns `IReactiveConfig<T>.CurrentValue`
