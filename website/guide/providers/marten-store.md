# Marten Store

`Cocoar.Configuration.WritableStore.Marten` is a ready-made [Writable Store](/guide/providers/writable-store) backend that persists overrides in [Marten](https://martendb.io/) (a PostgreSQL document store). Its headline feature is **tenant-aware, database-per-tenant** storage: with Marten multi-tenancy, each tenant's configuration overlay lives in that tenant's own database.

```shell
dotnet add package Cocoar.Configuration.WritableStore.Marten
```

It is an opt-in integration package — it intentionally takes a Marten dependency. Consumers who don't reference it pay nothing.

## Why it is service-backed

The backend needs a Marten `IDocumentStore`, which lives in the DI container — so the rule must resolve it *after* the container is built. That is exactly what [service-backed (Layer-2) configuration](/guide/di/service-backed) is for. Author the rule inside `UseServiceBackedConfiguration`, where `FromMartenStore()` is available:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseServiceBackedConfiguration(rules =>
    [
        rules.For<TenantSettings>().FromMartenStore().TenantScoped().Build(),
    ]));
```

`FromMartenStore()` resolves the `IDocumentStore` from DI and uses the current tenant (`accessor.Tenant`) to select the tenant database. The rule stays dormant until the host starts; the document store is never touched before the container exists.

Because it reuses the writable-store pipeline, you also get the `IWritableStore<TenantSettings>` write façade (per tenant) for writing overrides at runtime.

## Tenant-aware, database-per-tenant

Configure Marten with database-per-tenant multi-tenancy as you normally would (`MultiTenantedDatabases` / `AddSingleTenantDatabase`), then combine `FromMartenStore()` with `.TenantScoped()`:

```csharp
services.AddMarten(opts =>
{
    opts.MultiTenantedDatabases(x =>
    {
        x.AddSingleTenantDatabase(contosoConnectionString, "contoso");
        x.AddSingleTenantDatabase(globexConnectionString, "globex");
    });
    opts.RegisterDocumentType<CocoarConfigDocument>();
});
```

At recompute time, the backend opens its Marten session for `accessor.Tenant`, so a write for tenant `contoso` lands in Contoso's database and is invisible to `globex`. Each tenant pipeline keeps its own writable store, so overlays never alias across tenants. See [Multi-Tenancy](/guide/multi-tenancy/overview) for how tenant pipelines are built and consumed.

A `null`/blank tenant uses Marten's default tenant — the single-database case, when you use `FromMartenStore()` without `.TenantScoped()`.

## Storage model

Overrides are stored as one [`CocoarConfigDocument`](https://github.com/cocoar-dev/cocoar.configuration/tree/develop/src/Cocoar.Configuration.WritableStore.Marten) per configuration type:

- `Id` — the storage key (the configuration type's full name, e.g. `MyApp.Settings.TenantSettings`).
- `Json` — the sparse overlay JSON the writable store reads and writes.

Register the document type with Marten (`RegisterDocumentType<CocoarConfigDocument>()`) so its table is created in each tenant database, or rely on Marten's runtime auto-creation.

## Reactivity and HA

A write is reactive **within the writing process**: it signals the provider's change observable and the pipeline recomputes, so every `IReactiveConfig<T>` view on that instance updates. In a multi-instance (HA) deployment all pointing at the same database, a write on instance A does **not** automatically propagate to B/C — cross-instance reactivity needs a database notification (e.g. PostgreSQL `LISTEN/NOTIFY`) routed into the provider's change stream. That is a separate, additive enhancement and is not part of this backend.

## See also

- [Writable Store](/guide/providers/writable-store) — the override-layer concept and write API this builds on.
- [Service-Backed Configuration](/guide/di/service-backed) — why DB-backed rules are Layer-2.
- [Multi-Tenancy](/guide/multi-tenancy/overview) — per-tenant pipelines.
