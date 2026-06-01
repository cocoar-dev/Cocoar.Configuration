---
description: IContextResolver<TRequest,TContext> hydrating request DTOs into domain context, global/class/property registration levels, Scoped lifetime, evaluation pipeline for contextual flags
---

# Context Resolvers

Context resolvers bridge HTTP request data to your domain model. They turn a simple request DTO into a rich context object that flags and entitlements use for evaluation.

## The Problem

A contextual flag needs domain context to make a decision:

```csharp
public FeatureFlag<UserContext, bool> BetaFeature { get; }

BetaFeature = user => user.IsBeta && user.PlanTier == "pro";
```

But an HTTP request only carries an identifier — not the full `UserContext`. Something needs to hydrate the context.

This is where resolvers come in. They are the **only place** where side effects (database calls, API lookups, claim parsing) should happen. Flags themselves are [pure functions](/guide/flags/concepts#why-delegates) — they receive config and context, nothing else.

## IContextResolver\<TRequest, TContext\>

A resolver converts a request DTO into a domain context:

```csharp
public interface IContextResolver<TRequest, TContext>
{
    Task<TContext> ResolveAsync(TRequest request);
}
```

```csharp
public record UserIdRequest(string UserId);

public class UserByIdResolver(IUserRepository users) : IContextResolver<UserIdRequest, UserContext>
{
    public async Task<UserContext> ResolveAsync(UserIdRequest request)
    {
        var user = await users.GetByIdAsync(request.UserId);
        return new UserContext(user.Id, user.Email, user.PlanTier, user.IsBeta);
    }
}
```

When an HTTP request arrives with `{ "userId": "123" }`, the resolver loads the full user from the database and creates the `UserContext` that the flag delegate expects.

## Registration Levels

Resolvers are registered via the second parameter of `UseFeatureFlags()` / `UseEntitlements()`, which is a DI extension method (requires `Cocoar.Configuration.DI` or `Cocoar.Configuration.AspNetCore`).

Resolvers can be registered at three levels of specificity:

### Global

Fallback for all flag/entitlement properties with matching `TContext`:

```csharp
.UseFeatureFlags(
    flags => [
        flags.Register<AppFlags>(),
        flags.Register<BillingFlags>()
    ],
    resolvers => [
        resolvers.Global<UserByIdResolver>()
    ])
```

Every `FeatureFlag<UserContext, TResult>` across all classes uses `UserByIdResolver` unless overridden.

### Class-level

Applies to all contextual properties in one class:

```csharp
.UseFeatureFlags(
    flags => [flags.Register<AdminFlags>()],
    resolvers => [
        resolvers.For<AdminFlags>(r => r
            .Use<AdminByIdResolver>())
    ])
```

### Property-level

Most specific — overrides class and global for one property:

```csharp
.UseFeatureFlags(
    flags => [flags.Register<AppFlags>()],
    resolvers => [
        resolvers.For<AppFlags>(r => r
            .ForProperty(f => f.BetaByEmail).Use<UserByEmailResolver>())
    ])
```

### Priority

When evaluating a contextual flag, the resolver is selected by priority:

1. **Property-level** (most specific)
2. **Class-level**
3. **Global** (fallback)

## Resolver Lifetime

Resolvers are registered as **Scoped** in DI by default. One instance is created per request scope. This allows resolvers to depend on scoped services like `DbContext`:

```csharp
public class TenantByIdResolver(AppDbContext db) : IContextResolver<TenantIdRequest, TenantContext>
{
    public async Task<TenantContext> ResolveAsync(TenantIdRequest request)
    {
        var tenant = await db.Tenants.FindAsync(request.TenantId);
        return new TenantContext(tenant.Id, tenant.Tier, tenant.Region);
    }
}
```

You can customize the lifetime on individual resolver registrations:

```csharp
resolvers => [
    resolvers.Global<UserByIdResolver>().AsSingleton(),
    resolvers.For<AppFlags>(r => r
        .Use<TenantByIdResolver>().AsTransient())
]
```

Available lifetime methods: `.AsScoped()` (default), `.AsSingleton()`, `.AsTransient()`.

## How Evaluation Works

When a contextual flag is evaluated via the REST API or `IFeatureFlagEvaluator`:

1. The request body is deserialized to `TRequest`
2. The resolver is instantiated from DI
3. `ResolveAsync(request)` hydrates the domain context
4. The flag delegate is invoked with the context
5. The result is returned

```
POST /flags/AppFlags/BetaFeature
{ "userId": "123" }

→ UserByIdResolver.ResolveAsync({ UserId: "123" })
→ UserContext { Id: "123", IsBeta: true, PlanTier: "pro" }
→ BetaFeature(userContext) → true
→ { "value": true }
```

## Multiple Resolver Types

Different properties in the same class can use different resolver types:

```csharp
resolvers.For<AppFlags>(r => r
    .Use<UserByIdResolver>()                                  // Default for this class
    .ForProperty(f => f.BetaByEmail).Use<UserByEmailResolver>())  // Override for one property
```

The only requirement is that the resolver's `TContext` matches the flag property's `TContext`.
