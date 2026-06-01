---
description: Two-layer model — eager no-DI UseConfiguration plus lazy UseServiceBackedConfiguration whose (sp,a) factories resolve container services, activated by a hosted service
---

# ADR-006: DI-aware Configuration (Two-Layer Model)

**Status:** Accepted — implemented on `feature/multitenant`
**Date:** 2026-05-30
**Decision Makers:** Core Team
**Type:** Feature / Architecture
**Related:** ADR-005 (multi-tenancy), the "No-DI core" principle (CLAUDE.md), Microsoft `IConfiguration`/`IOptions`, the HTTP/WritableStore/Marten provider discussion

> **Implementation note (delivered).** Shipped as `UseServiceBackedConfiguration` (Layer 2) + `FromStore((sp,a)=>IStoreBackend)` (DI package) + `FromHttp((sp,a)=>HttpClient)` (Http package), activated by `ServiceBackedConfigurationActivator : IHostedLifecycleService` and the manual `IServiceProvider.ActivateServiceBackedConfigurationAsync()`. The sp-gate is a dedicated, non-clobberable `ConfigRuleOptions.ActivationGate` enforced in `RuleManager.ShouldSkip` (mirrors the `.TenantScoped()` marker — fluent-order-proof). Activation wiring lives in the DI **instance** overload `AddCocoarConfiguration(IServiceCollection, ConfigManager)` — the single point all entry paths (DI, AspNetCore, manual) funnel through.

> The `(sp,a)` overloads are **type-scoped, not ambient**: `UseServiceBackedConfiguration(rules => …)` hands each `rules.For<T>()` a public `ServiceBackedProviderBuilder<T> : TypedProviderBuilder<T>` carrying a public `ServiceBackedRuleContext` (`IsActive` + `ServiceProvider`). `FromStore`/`FromHttp((sp,a)=>…)` are extensions on *that* type, so using them in Layer-1 `UseConfiguration` is a **compile error**, not a runtime throw. The seam is **public**: a third-party provider package authors its own `FromX((sp,a)=>…)` extension on `ServiceBackedProviderBuilder<T>` (read `Context.ServiceProvider`, gate with the public `WithActivationGate(_ => Context.IsActive)`) and exposes a slot for the resolved artifact on its provider options. The provider class (`ConfigurationProvider<,>`) stays DI-free. Whether a provider is service-backable is the provider author's choice. §11 (scoped `ITenantReactiveConfig<T>` + `ITenantContext`) shipped in `Cocoar.Configuration.AspNetCore`. Covered by `Cocoar.Configuration.ServiceBacked.Tests` + AspNetCore tenant-adapter tests. See "Open questions" below for the resolved decisions.

---

## Context

### The No-DI core (which we keep)
`Cocoar.Configuration` (core) has **zero dependency on `Microsoft.Extensions.DependencyInjection`** — only `Microsoft.Extensions.Logging.Abstractions` + first-party packages. This is load-bearing, not decorative:
- **Test ergonomics** — the bulk of the suite uses `ConfigManager.Create(...)` directly, no `ServiceProvider`.
- **Embedding moat** — a *library* can use Cocoar internally without forcing a DI container on its consumers.
- **CLI / workers / AOT / alt-containers** — `Cocoar.Configuration.Secrets.Cli` is a real no-DI consumer; Autofac/Lamar/DryIoc shops stay supported via the thin `.DI` adapter.

**We do not delete or weaken the No-DI core.** This ADR adds DI capability *on top*, as an opt-in satellite.

### The limitation this ADR removes
`AddCocoarConfiguration(Action<ConfigManagerBuilder>)` today does:

```csharp
var configManager = ConfigManager.Create(configure); // builds AND initializes EAGERLY, here
services.AddCocoarConfiguration(configManager);       // registers the already-built INSTANCE (not a sp => factory)
```

`ConfigManager.Create` runs `Configure` **and** `Initialize` synchronously — instantiating every provider and running the initial recompute **before `BuildServiceProvider()` ever runs.** Consequences:

- Config providers are built **pre-container** → they **cannot resolve services from the app container.**
- Evidence: the HTTP provider does `new HttpClient()` (`HttpProvider.cs`); it cannot use `IHttpClientFactory`. The only DI seam (`IProviderServiceRegistration`) is **registration-time and one-way** ("Called once during DI setup — not on every recompute") — providers register services *into* DI, they cannot resolve *from* it at recompute time.
- The most important enterprise providers therefore can't be done cleanly: **DB-backed config** (Marten `IDocumentStore`, EF `IDbContextFactory<T>`) and **HTTP via `IHttpClientFactory`**.

### The hard logical boundary (true in every framework)
Config that needs a DI service *to load* cannot be used to *bootstrap the DI container* — that is circular. So **pre-container config must come from dependency-free sources** (file, env, command-line, static). This is not a Cocoar limitation; Microsoft has the same boundary.

### How Microsoft solves it (the pattern we follow)
A **two-layer** architecture:

| Layer | When | DI? |
|---|---|---|
| `IConfiguration` (raw key-values, sources) | eager, pre-container | **no** — dumb providers; a provider that needs a dependency (Key Vault credential, EF context) is **hand-fed** it / news its own |
| `IOptions<T>` / `IOptionsMonitor<T>` (typed binding + post-processing) | **lazy, resolved from the container** | **yes** — `IConfigureOptions<T>` are DI services that can inject dependencies |

Cocoar currently **fuses** both layers (providers + typed binding + reactive, all eager in the `ConfigManager`). That is more powerful in some ways (layering, transforms, reactive in one model) but it inherits the eager-source limitation **without** Microsoft's lazy `IOptions` escape hatch. This ADR adds that lazy layer — in Cocoar's own ordered-layer idiom.

> For the Marten/DB-per-tenant case, Cocoar with this layer would actually **exceed** Microsoft's built-ins: Microsoft's EF config provider news up its *own* `DbContext`; Cocoar would use the app's real, DI-managed `IDocumentStore`, tenant-scoped.

---

## Decision

Introduce a **two-layer configuration model**. The core stays DI-free; the DI integration is a **satellite extension on the DI package**, exactly like `UseSecretsSetup()` / `UseFeatureFlags()`.

### 1. Two authoring surfaces

```csharp
services.AddCocoarConfiguration(c => c
    // Layer 1 — eager, no IServiceProvider, available pre-container. Wires the DI plan + bootstrap config.
    .UseConfiguration(rule =>
    [
        rule.For<LogConfig>().FromFile("appsettings.json"),                 // bootstrap log level (eager)
        rule.For<Db>().FromFile(a => $"tenants/{a.Tenant}/db.json").TenantScoped(),  // tenant, no DI
    ])
    // Layer 2 — extension method FROM the DI package; rules whose factories receive the IServiceProvider.
    .UseServiceBackedConfiguration(rule =>
    [
        rule.For<LogConfig>().FromHttp((sp, a) =>
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("cocoar-config"), "logging.json"),
        rule.For<TenantSettings>().FromStore((sp, a) =>
            new MartenConfigBackend(sp.GetRequiredService<IDocumentStore>(), a.Tenant)).TenantScoped(),
    ]));
```

- `UseConfiguration` — **Layer 1**, core, unchanged. No `sp`.
- `UseServiceBackedConfiguration` — **Layer 2**, **defined in `Cocoar.Configuration.DI`** as an extension on `ConfigManagerBuilder`. Its rules' provider factories receive the `IServiceProvider`.

`IServiceProvider` never appears in the core public surface.

### 2. Mechanism — holder + per-rule `sp`-gate + activation hosted service

Three pieces, **all in the DI package**:

1. **`ServiceProviderHolder`** (DI-package singleton): `null` until the container is built; afterward holds the **root** `IServiceProvider`.
2. **`sp`-using factory overloads** (`FromStore((sp,a)=>…)`, `FromHttp` with `IHttpClientFactory`, …): each wraps a core provider-options factory `accessor => userFactory(holder.ServiceProvider!, accessor)` **and** composes a gate `.When(_ => holder.HasServiceProvider)`. The gate reuses the `ShouldSkip` machinery hardened in ADR-005 (a rule that skips while its precondition is absent, contributing nothing).
3. **An activation `IHostedService`** (registered by the DI package's `AddCocoarConfiguration`, where the `IServiceCollection` is available; it is container-constructed so it receives `sp`): on host start it sets `holder.ServiceProvider = sp` and triggers a **recompute from the Layer-2 start index**.

**Core touch is minimal:** reuse `ShouldSkip` (the `sp`-gate is expressed via the existing `When` predicate) and the already-internal `ScheduleRecompute(startIndex)` + `RestorePrefixContributions`. The only likely new core seam is a small **internal hook to append satellite-supplied rules** to the builder (consistent with how satellites already extend it). `InternalsVisibleTo("Cocoar.Configuration.DI")` already exists, so the DI package can drive the post-container recompute.

### 3. Lifecycle — two-phase for the global pipeline, single-phase for tenants

- **Global pipeline:** Layer 1 runs **eager** at registration (for the DI plan + bootstrap config). Layer-2 rules are **dormant** (`sp`-gated) until host start; the hosted service then sets the holder and triggers `ScheduleRecompute(layer2Index)` → Layer 2 activates, merges on top, reactive subscribers emit.
- **Tenant pipelines:** always built at runtime (`InitializeTenantAsync`, post-container, `sp` already present) → a **single** recompute runs Layer 1 + Layer 2 together; the `sp`-gate is automatically satisfied. The two-phase split is a *global-pipeline* concern only.

### 4. Precedence and gating are separable (key clarification)

"Layer 2" bundles two **independent** properties — keep them separate in the implementation:

- **Precedence** = position in the combined list. The Layer-2 bucket sits *after* Layer 1 → Layer 2 wins per key.
- **Gating** = **per rule**, and only for rules whose factory actually uses `sp`. A non-`sp` rule placed in Layer 2 is **not** gated → it runs eagerly **and** gains the later precedence.

Consequence — "a non-DI rule must beat a DI-backed rule" needs **no duplication**: declare the non-DI rule once, in Layer 2, *after* the DI-backed rule. It runs eagerly (no `sp`) and wins by position.

**Decision:** gate **per-`sp`-usage** (recommended), not per-bucket. Per-bucket is a simpler mental model but needlessly defers non-`sp` rules placed in Layer 2.

### 5. Tenancy is orthogonal to the layer

Two independent axes; the layer is chosen by `sp`-need, **not** by tenancy:

| | no `sp` (Layer 1) | needs `sp` (Layer 2) |
|---|---|---|
| **global** | `FromFile("app.json")` | `FromHttp((sp,a)=>factory…)` |
| **tenant** | `FromFile(a=>$"t/{a.Tenant}/db.json").TenantScoped()` *(works today)* | `FromStore((sp,a)=>new Marten(store,a.Tenant)).TenantScoped()` |

`.TenantScoped()` is a layer-agnostic modifier, valid in **both** methods. The gates **compose**: `.TenantScoped()` adds a "tenant present" gate; Layer 2 adds an "`sp` present" gate. **Marten-per-tenant** = both gates → runs only in a tenant pipeline post-container. Do **not** restrict tenant rules to Layer 2 — that would couple tenancy to DI and kill no-DI multi-tenant scenarios (file-per-tenant in a CLI / embedded lib).

### 6. Reactive contract (load-bearing)

- **Layer-2 activation is a RECOMPUTE on the existing pipeline (same backplane), never a rebuild.** A rebuild would orphan every previously-obtained reactive view.
- Therefore **all live `IReactiveConfig<T>` views receive the Layer-2 update, regardless of when they were obtained** — they are all views over the same `MasterBackplane.SnapshotStream`, and Layer-2 activation is just another committed snapshot. A view obtained *pre-container* (e.g. to drive a Serilog `LoggingLevelSwitch`) gets the Layer-2 value when it lands, then every subsequent poll change.

```csharp
var levelSwitch = new LoggingLevelSwitch();
var live = mgr.GetReactiveConfig<LogConfig>();              // pre-container is fine
live.Subscribe(c => levelSwitch.MinimumLevel = Map(c.Level));
// fires: now (Layer-1 file level) → on Layer-2 activation (remote level) → on every poll change after
```

Note: this requires a **subscription** (push), not a one-time `.CurrentValue` read. (Driving the actual MEL `ILogger` filters still needs an explicit bridge from the reactive value to `LoggerFilterOptions` — that is a logging-integration concern, not part of this ADR.)

### 7. Readiness contract (= `IOptions` semantics)

- Layer-2 values are **guaranteed after host start**.
- A snapshot read (`GetConfig<T>()`) **before** host start returns the **Layer-1 base** value; a reactive subscriber gets the upgrade when Layer 2 activates.
- A type that exists **only** in Layer 2 is unresolved (null) before host start.

### 8. Failure semantics

Layer-2 rules should typically be **optional**: if a Layer-2 source fails (DB/HTTP down), the recompute rolls back to the last good state → **Layer-1 values persist**, health is degraded. A remote outage must not nuke the whole config.

### 9. Lifetime discipline (the holder is the ROOT provider)

The holder's `sp` is the **root** `IServiceProvider` (the activation hosted service is root-constructed; we are **not** in a request scope). Therefore:

- Resolve **singletons / factories only** — `IDocumentStore` (Marten), `IDbContextFactory<T>` (EF), `IHttpClientFactory`. Open **short-lived units per read** on the recompute thread (`store.QuerySession(a.Tenant)`, `factory.CreateDbContext()`, `factory.CreateClient(...)`). **Never** resolve a scoped service from root (captive-dependency bug).
- This is correct, not a limitation: config is computed **once per tenant/global, cached, reactive — not per request.** The request scope is irrelevant to a config recompute. (If a source ever genuinely needs a scoped service, create a scope per recompute — rarely needed.)

### 10. HTTP provider gains an `IHttpClientFactory`-backed path

Today `HttpProvider` does `new HttpClient()`. Add a Layer-2 overload that resolves `IHttpClientFactory` from the holder and uses a named client — gaining handler pooling/rotation, Polly via `AddHttpClient`, etc. The current `new HttpClient()` / `HttpMessageHandler?` path stays for Layer 1 / no-DI.

### 11. Consumption-tenant adapter (implemented)

Distinct from the **source-tenant** flow above (`a.Tenant`, build side) is the **consumption-tenant** flow: "this request's tenant's config via injection." A *separate* concern from this ADR's core, built on top of the existing `GetReactiveConfigForTenant`:

- The **`ITenantContext { string? Current }`** abstraction ("who is the current tenant for this request/scope") is **ambient tenant resolution** — a container/scope concern, so it lives in **`Cocoar.Configuration.DI`**. No-DI hosts have no ambient scope; they pass the tenant explicitly via `…ForTenant(id)`.
- **DI:** a scoped **`ITenantReactiveConfig<T>`** adapter (in **`Cocoar.Configuration.DI`**) reads `ITenantContext.Current` and delegates to `mgr.GetReactiveConfigForTenant<T>(tenant)`. The app registers a scoped `ITenantContext` with `AddCocoarTenantResolver<TService>(s => s.TenantId)` — pointing at whatever already knows the tenant, no adapter to hand-write. HTTP is simply `AddCocoarTenantResolver<IHttpContextAccessor>(a => a.HttpContext?...)`; there is **no** AspNetCore-specific resolver API.
- Scoped/transient consumers only; a singleton can never have an ambient tenant → it uses explicit `GetReactiveConfigForTenant(id)`.
- **Trap:** do **not** re-register `IReactiveConfig<T>` itself as scoped (it is a singleton; that would break singletons injecting it). Use a **distinct** `ITenantReactiveConfig<T>`.

---

## Non-breaking guarantees

Existing consumers (Layer-1-only) are untouched, **if** we hold three rules:

1. **Layer 1 (`UseConfiguration`) stays eager and identical** — readiness, timing, even the "I/O at registration" behavior. Only the new opt-in Layer 2 is lazy.
2. **Everything new is additive** — new builder extension, new `(sp,a)=>` factory overloads, new types. **No existing signature changes** (do not touch `IConfigurationAccessor` / `ConfigurationProvider` in a breaking way).
3. **The activation hosted service is registered only when Layer-2 rules exist** → zero impact for apps that do not opt in.

Plus the §11 trap: never re-register `IReactiveConfig<T>` as scoped.

---

## Engine / package impact

| Area | Change | Kind |
|---|---|---|
| Core `RuleManager.ShouldSkip` | reused for the `sp`-gate (via the existing `When` predicate) — no change needed | Reuse |
| Core `ConfigurationEngine.ScheduleRecompute(startIndex)` + `RestorePrefixContributions` | reused to run the Layer-2 activation recompute | Reuse |
| Core `ConfigManagerBuilder` | likely **one small internal hook** to append satellite-supplied rules | Additive (internal) |
| **NEW** `Cocoar.Configuration.DI`: `ServiceProviderHolder` + `UseServiceBackedConfiguration` extension + `sp`-aware factory overloads (`FromStore`, …) + activation `IHostedService` | the whole Layer-2 mechanism | **New (satellite)** |
| `Cocoar.Configuration.Http` | `FromHttp((sp,a)=>…)` overload resolving `IHttpClientFactory` | Additive |
| `Cocoar.Configuration.DI` | scoped `ITenantReactiveConfig<T>` + `AddCocoarTenantResolver<TService>` (§11) | Additive |

**Net:** the core gains essentially nothing DI-specific (a small internal append hook at most); the entire DI integration lives in the satellite packages. The No-DI core is preserved.

---

## Consequences

✅ DB-backed config (Marten/EF) and `IHttpClientFactory`-backed HTTP become possible — the headline enterprise scenarios
✅ Marten-per-tenant config falls out of composing the tenant gate + the `sp` gate
✅ Bootstrap config (eager, Layer 1) + remote/DI-backed override (lazy, Layer 2) in **one** reactive value — nicer than juggling `IConfiguration`/`IOptions`/`IOptionsMonitor`
✅ No-DI core preserved; fully additive/opt-in; non-breaking for existing consumers
✅ Removes a latent smell: today file/HTTP I/O runs at *service registration* time; Layer-2 work moves to container-owned time

⚠️ A readiness contract exists (Layer-2 values after host start) — must be documented; consumers needing the upgrade must subscribe, not snapshot
⚠️ The activation timing vs. consumers resolved *during* `BuildServiceProvider` needs care (hosted service runs after build, before serving)
⚠️ DB sources have no push change-detection by default → poll or Postgres `LISTEN/NOTIFY` (separate work)
⚠️ Precedence is bucketed (all Layer 1 before all Layer 2); the rare "non-DI must beat DI-backed" is handled by placing the non-DI rule in Layer 2 (§4), not across the bucket boundary

---

## Open questions — resolved in the implementation

- **Gating granularity:** ✅ per-`sp`-usage. Each `sp`-using overload attaches a dedicated `ActivationGate`; a non-`sp` rule placed in Layer 2 runs eagerly and still wins by position.
- **Naming:** ✅ `UseServiceBackedConfiguration`; factory overloads `FromStore((sp,a)=>IStoreBackend)` and `FromHttp((sp,a)=>HttpClient)`.
- **Activation hook:** ✅ `IHostedLifecycleService`, acting in `StartingAsync` (before any regular `IHostedService.StartAsync`), so Layer 2 is live before app/hosted-service code reads config. A manual `IServiceProvider.ActivateServiceBackedConfigurationAsync()` covers non-host scenarios; both are idempotent (the holder publishes the provider exactly once). Consumers that read a snapshot *during* container build see the Layer-1 base; the readiness contract (§7) requires a **subscription** to receive the upgrade.
- **Append-rules core seam:** ✅ `ConfigManagerBuilder.AddServiceBackedRules(IEnumerable<ConfigRule>)` appends after Layer 1 and records `ConfigManager.ServiceBackedLayerStartIndex`. The sp-gate seam is the **public**, **type-scoped** (not ambient) `ServiceBackedRuleContext` (BCL `IServiceProvider` only — the core never names a DI type): it is carried by the public `ServiceBackedProviderBuilder<T>.Context` and read by the DI, Http, and third-party `(sp,a)` overloads.
- **DB change-detection:** still out of scope here — poll (via `FromStore` on a polling backend) or app-driven re-init (`RemoveTenantAsync` then `InitializeTenantAsync`; there is no in-place reload). Push (`LISTEN/NOTIFY`) remains separate, future work.

---

## Alternatives considered

- **Make DI mandatory / delete the No-DI core** — rejected. The No-DI core is the test/CLI/embedding/alt-container moat; the DI majority is served by making the DI path the blessed default, not by removing No-DI.
- **Make everything lazy (Layer 1 too)** — rejected. Breaks config-driven DI registration (you must read config *while* building the `ServiceCollection`), breaks "config ready when `AddCocoarConfiguration` returns", and would be a breaking change.
- **`sp` on the outer rule-list lambda (`(rule, sp) => […]`)** — rejected. The DI plan needs the Layer-2 *type list* at registration (no `sp`); `sp` must flow into the provider *factories*, not the enumerable lambda.
- **Build an intermediate `ServiceProvider` at registration to feed providers** — rejected (well-known anti-pattern: a second container, duplicate singletons, disposal chaos).
- **Restrict tenant rules to Layer 2** — rejected (couples tenancy to DI; kills no-DI multi-tenant; §5).

---

## References

- ADR-005 — multi-tenancy (the `.TenantScoped()` gate + `GetReactiveConfigForTenant` this builds on)
- `src/Cocoar.Configuration.DI/CocoarConfigurationExtensions.cs` — the eager `ConfigManager.Create` + `AddSingleton(instance)` to be made container-owned for Layer 2
- `src/Cocoar.Configuration.Http/HttpProvider.cs` — the `new HttpClient()` to get an `IHttpClientFactory` overload
- `src/Cocoar.Configuration/Rules/RuleManager.cs` — `ShouldSkip` (the gate machinery)
- `src/Cocoar.Configuration/Core/ConfigurationEngine.cs` — `ScheduleRecompute(startIndex)` + `RestorePrefixContributions` (the activation recompute)
- Microsoft `IConfiguration` / `IOptions` — the proven two-layer precedent
