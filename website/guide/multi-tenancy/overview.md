# Multi-Tenancy

Multi-tenant applications need the **same configuration type to resolve to different values per tenant** — a global default for everything, with each tenant overriding only the keys it sets and inheriting the rest.

Cocoar.Configuration models this as **per-tenant pipeline bundles layered on a shared global base** (see [ADR-005](https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/adr/ADR-005-multi-tenant-configuration.md)). You author **one flat rule list** and mark the per-tenant rules with `.TenantScoped()`; the tenant id flows in through the configuration accessor. There is no second authoring surface and no provider becomes "tenant-aware".

::: tip When do I need this?
Only when one process serves many tenants and the **same type** must differ per tenant at runtime, with tenants added/removed dynamically. A single-tenant app needs none of this — the global pipeline is unchanged.
:::

## The two primitives

- **`IConfigurationAccessor.Tenant`** — `null` in the global (tenant-agnostic) pipeline, the tenant id inside a tenant pipeline. Tenant-varying rule factories interpolate it.
- **`.TenantScoped()`** on a rule — the rule runs **only** for a tenant and is skipped in the global pipeline. It is shorthand for `.When(a => !string.IsNullOrWhiteSpace(a.Tenant))`.

```csharp
var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
[
    // Global base — applies to everything, injectable as usual:
    rules.For<SmtpSettings>().FromStaticJson(smtpDefaults),

    // Per-tenant overlay — wins per key, inherits the rest. The id flows via the accessor:
    rules.For<SmtpSettings>().FromFile(a => $"tenants/{a.Tenant}/smtp.json").TenantScoped(),
]));
```

The effective value for a tenant is `[global rules] ++ [tenant-scoped rules]`, run through the **same** recompute/merge pipeline as any config — so transforms, required-rule rollback and dependency ordering all behave identically. Placing a global rule **after** the tenant overlay makes it a non-negotiable platform ceiling (it wins over the tenant) — no special tier, just list position.

## Lifecycle

The host owns the tenant list. A tenant's configuration is materialized **on demand** and async is confined to that init moment — reads stay synchronous, exactly like the global config.

```csharp
var tenants = (ITenantConfigurationAccessor)manager;       // ConfigManager implements it

await tenants.InitializeTenantAsync("acme");               // build the tenant pipeline (at tenant creation)
await tenants.EnsureTenantInitializedAsync("acme");        // idempotent warmup (e.g. request-start middleware)
bool ready = tenants.IsTenantInitialized("acme");
await tenants.RemoveTenantAsync("acme");                   // dispose the tenant bundle (at tenant removal)
```

`InitializeTenantAsync` is idempotent and safe under concurrency — a tenant is built exactly once.

## Consuming a tenant's configuration

Tenant-scoped values are obtained by **passing the tenant id**, never by DI injection:

```csharp
var smtp  = manager.GetConfigForTenant<SmtpSettings>("acme");          // sync read
var live  = manager.GetReactiveConfigForTenant<SmtpSettings>("acme");  // IReactiveConfig<T> for this tenant
var flags = manager.GetFeatureFlagsForTenant<BillingFlags>("acme");
var ents  = manager.GetEntitlementsForTenant<PlanEntitlements>("acme");
var store = manager.GetWritableStoreForTenant<SmtpSettings>("acme");    // per-tenant write facade
```

### Not DI-injectable — by design

A type whose **every** rule is `.TenantScoped()` has no global value. Injecting it into a long-lived (singleton) consumer would be a captive-dependency bug — it would freeze one tenant forever, since the container cannot know the runtime tenant. The DI planner therefore **excludes** purely tenant-scoped types from the global plan. A type that *also* has a global base rule stays injectable (its base value is a valid global config). Consuming services inject the `ConfigManager` / `ITenantConfigurationAccessor` and call `…ForTenant(currentTenant)`.

### Scoped per-request injection (ASP.NET Core)

So scoped/transient services don't have to thread the tenant id by hand, `Cocoar.Configuration.AspNetCore` offers a **scoped** `ITenantReactiveConfig<T>` that resolves the *current request's* tenant for you. You supply a scoped `ITenantContext` (only your app knows where the tenant lives — a claim, header, or route value); the adapter delegates to `GetReactiveConfigForTenant<T>(tenant)`.

```csharp
// Register the adapter + a default ITenantContext that reads the tenant from the request:
builder.Services.AddCocoarTenantReactiveConfig(http => http.Request.RouteValues["tenant"]?.ToString());

// Ensure the tenant pipeline is warm before it is consumed (e.g. request-start middleware):
app.Use(async (ctx, next) =>
{
    if (ctx.Request.RouteValues["tenant"] is string t)
        await app.Services.GetRequiredService<ConfigManager>().EnsureTenantInitializedAsync(t);
    await next();
});

// In any scoped/transient service — no tenant id threaded by hand:
public sealed class SmtpSender(ITenantReactiveConfig<SmtpSettings> smtp)
{
    public void Send() => Connect(smtp.CurrentValue.Host);   // this request's tenant
}
```

The singleton `IReactiveConfig<T>` is **untouched** — it stays the global view, so singletons keep working. A singleton that needs a specific tenant still calls `GetReactiveConfigForTenant<T>(id)` explicitly (it has no ambient request tenant).

## Feature flags & entitlements per tenant

The same source-generated flag/entitlement class is constructed with the **tenant's** `IReactiveConfig<T>`, so it evaluates against that tenant's effective config — **no source-generator change**:

```csharp
public partial class BillingFlags : IFeatureFlags<BillingConfig>
{
    public FeatureFlag<bool> PremiumEnabled => () => Config.PremiumBilling;
}

bool premium = manager.GetFeatureFlagsForTenant<BillingFlags>("acme").PremiumEnabled();
```

In ASP.NET Core, map the tenant-dimensioned REST endpoints (a `{tenant}` route segment; the handler warms the tenant up and evaluates per tenant):

```csharp
app.MapTenantFeatureFlagEndpoints();   // GET /tenants/{tenant}/flags/{FlagClass}/{FlagName}
app.MapTenantEntitlementEndpoints();   // GET /tenants/{tenant}/entitlements/{Class}/{Name}
```

## Per-tenant WritableStore

Give each tenant its own backend via the factory overload (the store is keyed by `accessor.Tenant`), and write through the per-tenant facade:

```csharp
rules.For<SmtpSettings>().FromStore((a, _) => BackendFor(a.Tenant)).TenantScoped()

await manager.GetWritableStoreForTenant<SmtpSettings>("acme").SetAsync(x => x.Port, 587);
```

A write triggers only that tenant's recompute; other tenants are untouched. Provenance (`DescribeAsync`) is computed over the tenant's own layers.

### DB-backed config per tenant

When the per-tenant source is a database (Marten / EF) reached through a DI-managed store, use `FromStore((sp, a) => …).TenantScoped()` — the tenant gate and the service-provider gate compose, so the rule runs only inside a tenant pipeline, after the host has started. See [Service-Backed Configuration](/guide/di/service-backed#db-backed-config-with-fromstorage).

## Per-tenant secrets

Per-tenant secrets reuse the existing **multi-kid certificate folder** — `kid = tenant`. Lay certificates out as `certsRoot/{tenant}/cert.pfx` and each tenant's overlay carries an envelope tagged with its own kid:

```csharp
c.UseSecretsSetup(secrets => secrets.UseCertificatesFromFolder(certsRoot));

using var lease = manager.GetConfigForTenant<VaultConfig>("acme").ApiKey!.Open();  // decrypts via certsRoot/acme
```

A tenant decrypts its own secret with its own certificate; it cannot decrypt another tenant's.

## Fan-out: global changes reach tenants automatically

Each tenant pipeline runs the full rule list with its **own** provider subscriptions, so a change to a live global base source (file / observable / HTTP) propagates to every initialized tenant on its own debounced recompute and re-emits on that tenant's `IReactiveConfig<T>`. A tenant that masks the changed key with its own override does not emit. No coordinator to configure; consistency is **per-tenant eventual** (a global change lands tenant-by-tenant as each rebuild finishes).

## Limits in this version

- **Mixed-scope tuples** — `IReactiveConfig<(Global, TenantScoped)>` / `IFeatureFlags<(Global, TenantScoped)>` are **not supported** (they would show transient skew during fan-out). Use same-scope tuples.
- **Resource use** scales linearly with initialized tenants × base rules (each tenant re-runs the base). Acceptable for a host-bounded active-tenant set; a shared seed-from-global optimization is a future, API-compatible change.
- **Eviction** is explicit (`RemoveTenantAsync`) only — no idle eviction.
