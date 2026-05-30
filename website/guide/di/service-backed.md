# Service-Backed Configuration

Some configuration sources need a service from your application container *to load* — an `IHttpClientFactory`, a Marten `IDocumentStore`, an EF `IDbContextFactory<T>`. But `AddCocoarConfiguration` runs **before** `BuildServiceProvider()`, so those services don't exist yet. This is a hard boundary in every framework: config that needs the container can't also *bootstrap* the container.

Cocoar solves it the same way Microsoft splits `IConfiguration` (eager, dumb sources) from `IOptions<T>` (lazy, DI-bound) — with a **two-layer model**, in Cocoar's own ordered-layer idiom (see [ADR-006](https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/adr/ADR-006-di-aware-configuration.md)).

| Layer | Method | When | `IServiceProvider`? |
|---|---|---|---|
| **Layer 1** | `UseConfiguration` | eager, at registration (wires the DI plan + bootstrap config) | **no** — file/env/static/HTTP-without-DI |
| **Layer 2** | `UseServiceBackedConfiguration` | lazy, on host start | **yes** — factories receive the container |

Layer 1 is unchanged and stays DI-free. Layer 2 is an additive, opt-in extension from `Cocoar.Configuration.DI`; the No-DI core never sees an `IServiceProvider`.

::: tip When do I need this?
Only when a provider must resolve an application service to load — DB-backed config or HTTP via `IHttpClientFactory`. File/env/static and the plain `FromHttp(url)` provider stay in Layer 1.
:::

## The two authoring surfaces

```csharp
services.AddCocoarConfiguration(c => c
    // Layer 1 — eager, no IServiceProvider, available before the container is built.
    .UseConfiguration(rules =>
    [
        rules.For<LogConfig>().FromFile("appsettings.json"),           // bootstrap log level
    ])
    // Layer 2 — extension from the DI package; factories receive the IServiceProvider.
    .UseServiceBackedConfiguration(rules =>
    [
        rules.For<LogConfig>().FromHttp(
            (sp, a) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cocoar-config"),
            "logging.json", pollInterval: TimeSpan.FromSeconds(30)),

        rules.For<TenantSettings>().FromStorage(
            (sp, a) => new MartenConfigBackend(sp.GetRequiredService<IDocumentStore>(), a.Tenant))
            .TenantScoped(),
    ]));
```

Layer-2 rules merge **after** Layer-1 rules — they win per key, exactly like any later rule. Each `(sp, a)` factory receives the application `IServiceProvider` and the current `IConfigurationAccessor` (its `Tenant` is set inside a tenant pipeline).

## HTTP via `IHttpClientFactory`

`FromHttp((sp, a) => HttpClient, url, …)` (from `Cocoar.Configuration.Http`) sources its client from the container — gaining handler pooling/rotation and `AddHttpClient` policies (Polly). The provider does **not** dispose a factory-supplied client.

```csharp
services.AddHttpClient("cocoar-config")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler())
    .AddPolicyHandler(retryPolicy);

services.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromFile("appsettings.json") ])
    .UseServiceBackedConfiguration(rules =>
    [
        rules.For<RemoteConfig>().FromHttp(
            (sp, a) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cocoar-config"),
            "https://config.internal/remote.json", pollInterval: TimeSpan.FromSeconds(30)),
    ]));
```

The plain `FromHttp(url)` overload (which `new`s its own `HttpClient`) stays available for Layer 1 / no-DI.

## DB-backed config with `FromStorage`

`FromStorage((sp, a) => IStorageBackend)` reuses Cocoar's storage pipeline: implement `IStorageBackend` (`ReadAsync`/`WriteAsync` over your store) and source it from DI. Combine with `.TenantScoped()` for **DB-config-per-tenant** — the tenant gate and the service-provider gate compose, so the rule runs only inside a tenant pipeline, after the host has started.

```csharp
public sealed class MartenConfigBackend(IDocumentStore store, string? tenant) : IStorageBackend
{
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        // Open a SHORT-LIVED unit per read on the recompute thread (never hold a session):
        await using var session = store.QuerySession(tenant ?? "");
        var doc = await session.LoadAsync<ConfigDoc>(key, ct);
        return doc?.Json is { } json ? Encoding.UTF8.GetBytes(json) : null;
    }

    public Task WriteAsync(string key, byte[] data, CancellationToken ct = default) => /* … */;
}
```

This **exceeds** Microsoft's EF config provider, which `new`s its own `DbContext`: here you use the app's real, DI-managed, tenant-scoped store.

## Deriving config from a DI service — `FromService`

When the config simply **comes from a DI service** (no I/O source — an in-memory registry, a computed default, another service's value), you don't need a custom provider at all. `FromService<TService>(s => …)` resolves the service from the container and projects it to the config value:

```csharp
.UseServiceBackedConfiguration(rules =>
[
    rules.For<AppSettings>().FromService<AppSettingsService>(s => s.Settings),
])
```

This is Cocoar's equivalent of Microsoft's `services.Configure<TDep>((opts, dep) => …)` / an `IConfigureOptions<T>` with an injected dependency — and the natural target when migrating those. The service is resolved at recompute time (after host start); the rule is dormant until then, like any Layer-2 rule, and composes with `.TenantScoped()`.

::: warning Synchronous / in-memory only
`FromService` snapshots once per recompute (no change detection) and the projection is synchronous. For I/O-bound sources (DB, HTTP, Key Vault) use an async provider — `FromStorage`, `FromHttp((sp,a)=>…)`, or a custom provider — rather than blocking inside the projection.
:::

## Lifecycle & the readiness contract

Layer 2 activates on **host start**. A `IHostedLifecycleService` publishes the root `IServiceProvider` and triggers a **recompute** (never a rebuild) from the Layer-2 boundary — Layer 1 stays stable, the Layer-2 suffix runs and merges on top.

- Layer-2 values are **guaranteed after host start**.
- A snapshot read (`GetConfig<T>()`) **before** host start returns the **Layer-1 base**; a type that exists *only* in Layer 2 is unresolved (`TryGetConfig` returns `false`).
- Because activation is a recompute on the same backplane, **every live `IReactiveConfig<T>` view receives the Layer-2 value when it lands — even views obtained before the container was built.**

```csharp
// Wire a Serilog level switch during bootstrap, BEFORE the host runs:
var live = configManager.GetReactiveConfig<LogConfig>();
live.Subscribe(c => levelSwitch.MinimumLevel = Map(c.Level));
// fires: now (Layer-1 file level) → on host start (Layer-2 remote level) → on every poll change after
```

::: warning Subscribe, don't snapshot
To receive the Layer-2 upgrade you must **subscribe** (`IReactiveConfig<T>`), not read a one-time `GetConfig<T>()` / `.CurrentValue` during container build.
:::

## Failure semantics

Layer-2 rules are **optional** by default: if the source is down (DB/HTTP unreachable), the recompute rolls back to the last good state, **Layer-1 values persist**, and health goes degraded. A remote outage never faults host startup or nukes your config.

## Lifetime discipline

The holder's `sp` is the **root** provider. Resolve **singletons / factories only** (`IDocumentStore`, `IDbContextFactory<T>`, `IHttpClientFactory`) and open **short-lived units per read** on the recompute thread (`store.QuerySession(…)`, `factory.CreateDbContext()`). Never resolve a scoped service from root — config is computed once per tenant/global, cached and reactive, not per request.

## Precedence vs. gating

These are separate. **Precedence** is list position (Layer 2 after Layer 1 → wins per key). **Gating** is per-rule and applies only to rules that actually use `sp`. A non-`sp` rule placed in Layer 2 runs eagerly *and* gains the later precedence — so "a non-DI rule must beat a DI-backed rule" is just: declare it once, in Layer 2, after the DI-backed rule.

## Activation without a Host

For apps that build their own `IServiceProvider` without an `IHost`, activate manually with the **root** provider:

```csharp
var provider = services.BuildServiceProvider();
await provider.ActivateServiceBackedConfigurationAsync();   // publishes sp + runs the Layer-2 recompute
```

It is idempotent with the automatic hosted-service activation and a no-op when no Layer-2 rules were registered.

## Custom (third-party) service-backed providers

Whether a provider can be service-backed is **entirely the provider author's choice** — the framework just offers the seam. `UseServiceBackedConfiguration(rules => …)` hands each `rules.For<T>()` a public `ServiceBackedProviderBuilder<T>`. Author your own `(sp, a) =>` overload on it via the `ServiceBacked(...)` helper — `sp` arrives as a **parameter** invoked lazily at recompute time, and the rule is gated for you:

```csharp
// In your provider package — uses only the public surface (no internals):
public static ProviderRuleBuilder<MyProvider, MyOptions, MyQuery> FromMyDb<T>(
    this ServiceBackedProviderBuilder<T> builder,
    Func<IServiceProvider, IConfigurationAccessor, MyBackend> backendFactory) where T : class
    => builder.ServiceBacked<MyProvider, MyOptions, MyQuery>(
        (sp, a) => new MyOptions(backendFactory(sp, a)),   // sp is a param, resolved lazily — never read too early
        _ => MyQuery.Default);
```

Two things make a provider service-backed: (1) author this `(sp, a)` overload on `ServiceBackedProviderBuilder<T>`, and (2) have the provider's **options carry** the resolved artifact (HTTP carries a `ClientFactory`; LocalStorage an `IStorageBackend`). The provider class itself (`ConfigurationProvider<,>`) stays DI-free — and a service-backed provider is usually its **own** small provider, not a no-DI one retrofitted with fallbacks. See [Building Custom Providers → Service-Backed Providers](/guide/providers/custom#service-backed-providers-di-aware) for a full worked example.

Because these overloads target `ServiceBackedProviderBuilder<T>`, using them inside the Layer-1 `UseConfiguration` (a plain `TypedProviderBuilder<T>`) is a **compile error** — the type system, not a runtime check, keeps DI-backed loading out of Layer 1.

## See also

- [Multi-Tenancy](/guide/multi-tenancy/overview) — `.TenantScoped()` and consuming a tenant's config (`ITenantReactiveConfig<T>`)
- [ASP.NET Core](/guide/di/aspnetcore)
- [ADR-006](https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/adr/ADR-006-di-aware-configuration.md) — the design rationale
