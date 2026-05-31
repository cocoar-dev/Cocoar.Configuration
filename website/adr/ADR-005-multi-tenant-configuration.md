# ADR-005: Multi-Tenant Configuration

**Status:** Accepted — implemented on `feature/multitenant`
**Date:** 2026-05-29 (updated 2026-05-30)
**Decision Makers:** Core Team
**Type:** Feature / Architecture
**Related:** ADR-001 (Capabilities), ADR-002 (Atomic Reactive Updates), ADR-004 (Aggregate Rules), PR #47 (WritableStore sparse override overlay), Secrets encryption-key publishing

---

## Context

Multi-tenant applications need the **same configuration type to resolve to different values per tenant**. Three kinds of configuration coexist:

1. **Global-only** — e.g. the master-table connection, the service's bind IP/port. One value, no tenant.
2. **Tenant-scoped** — valid per tenant.
3. **Global, but per-tenant overridable** (the common, preferred case) — a global value for everything, where a tenant overrides only the keys it sets and inherits the rest.

### Constraints from the target environment

- The host owns the tenant list (e.g. Marten with db-per-tenant); tenant connection strings live there, **not** in config.
- **Tenants are added and removed at runtime** — configuration cannot be static or precomputed for a fixed tenant set.
- Therefore **Cocoar.Configuration must be tenant-list-agnostic**: it is handed a tenant id and builds that tenant's configuration **on demand**, never enumerating or syncing a tenant registry.

### Feasibility (engine-verified)

The recompute/state/reactive engine was audited against this model. All pipeline building blocks (`ConfigurationEngine`, `ConfigurationState`, `MasterBackplane`, `ConfigSnapshot`, `ConfigJsonRepository`, `RecomputeScheduler`, `RuleManager`, `BackplaneReactiveConfig<T>`) are **per-instance with no static singletons**, and the merge primitive (`MutableJsonMerge.Merge`, ordered last-write-wins — already used by `ConfigManager.BuildBaseJson`) already expresses the required layering. The model is feasible without rewriting the recompute, snapshot, or reactive cores.

---

## Decision

Introduce a **tenant dimension** carried by a per-tenant rule factory and a per-tenant pipeline bundle layered on the shared global state.

### 1. Declaration — one flat rule list, per-rule `.TenantScoped()`

> **Settled authoring API.** An earlier draft of this ADR used a `ForEachTenant((r, tenant) => …)` block. That, and a tiered builder, a `{tenant}` path token, and a top-level `(c, tenant)` lambda, were all rejected (see *Alternatives considered*). The final shape keeps **one flat rule list** and adds exactly one new primitive — a tenant marker on the rule — plus a tenant id on the accessor.

Two pieces:

- **`Tenant` on `IConfigurationAccessor`** — a default-interface member returning `null` in the global pipeline and the tenant id in a tenant pipeline. Tenant-varying rule factories interpolate it; `.TenantScoped()` keys off it.
- **`.TenantScoped()` on the rule builder** — marks a rule to run **only** when a tenant is present (skipped in the global, tenant-agnostic pipeline). Shorthand for `.When(a => !string.IsNullOrWhiteSpace(a.Tenant))`, AND-composed with any existing `When`.

Existing providers compose unchanged — a tenant-varying source is just the existing `Func<IConfigurationAccessor, T>` factory with the id interpolated. No provider becomes "tenant-aware", and no new `Func<string, …>` overloads are added.

```csharp
services.AddCocoarConfiguration(c => c.UseConfiguration(rules =>
[
    // Global-only (single state, injectable as today):
    rules.For<MasterDbSettings>().FromStaticJson(masterDefaults),

    // Global base for a type that is ALSO tenant-overridable:
    rules.For<SmtpSettings>().FromStaticJson(smtpDefaults),
    rules.For<SmtpSettings>().FromStore(),                 // global app-override

    // Tenant-scoped overlays — same flat list, marked .TenantScoped(); the id flows via the accessor:
    rules.For<SmtpSettings>().FromFile(a => $"tenants/{a.Tenant}/smtp.json").TenantScoped(),
    rules.For<SmtpSettings>().FromStore((a, _) => BackendFor(a.Tenant)).TenantScoped(),   // per-tenant backend
]));
```

A `.TenantScoped()` rule is registered once but contributes nothing in the global pipeline (its `When` is false there); in a tenant pipeline the same rule runs with `Tenant = id`. There is **no resolver in the definition** — the tenant id is supplied at query time (see §5).

### 2. Per-tenant pipeline bundle on a shared global base

The single global `ConfigManager` keeps exactly one global pipeline (unchanged, byte-identical). Each initialized tenant gets its own **pipeline bundle** (engine + state + backplane + reactive manager + rule managers), held in a `ConcurrentDictionary<tenantId, TenantPipeline>`. Tenant pipelines share the single, read-only `ExposureRegistry`. `ConfigSnapshot` stays keyed by `Type` only — the tenant dimension is the registry key, not a composite snapshot key.

### 3. Rule composition — FLATTEN, not blob-overlay

For a tenant-scoped type `T`, the effective layer stack is the **flattened rule list** `[global rules for T] ++ [tenant rules for T]`, run through the **same recompute pipeline** as a normal config.

> We do **not** merge a pre-computed tenant-JSON blob onto a pre-computed global-JSON blob. The recompute pipeline is the single proven path that turns an ordered rule list into a value — including transforms (`Mount`/`Select`), required-rule rollback, and dependency ordering. Flattening reuses that one path; a "merge two blobs" step would fork it and break those transforms.

The tenant segment is a **positioned segment** of the flattened list. Placing it last (the default) gives "tenant wins per key, else inherits global". Placing a global rule after it would let a global value override tenants (forced/compliance policy) — supported by construction, not a special case.

> **v1 implementation: full-list-per-tenant (seed-from-global deferred).** Each tenant pipeline runs the **entire** flat rule list — global rules included — with its own rule managers and providers. This is correct and, crucially, gives **automatic fan-out** (§6): each tenant holds its own subscription to a live global base source, so a base change reaches the tenant with no cross-pipeline machinery. The cost is linear: N tenants re-run the base rules. The originally-planned **seed-from-global** optimization (read the global managers' last contribution lock-free and recompute only the tenant-scoped suffix) is **deferred** — it would save the re-run but trade away automatic fan-out for an explicit coordinator and a lock-ordering hazard against the global recompute semaphore. It remains a clean, isolated optimization to add behind the unchanged public API if tenant/rule counts ever make the re-run cost matter.

### 4. Lifecycle — explicit, async at init, sync at read

Mirrors `ConfigManager.CreateAsync` (build async, then serve sync), scoped per tenant:

```csharp
await mgr.InitializeTenantAsync(tenantId);          // build the tenant's pipeline (async), at tenant creation
await mgr.EnsureTenantInitializedAsync(tenantId);   // idempotent warmup (e.g. request-start middleware)
await mgr.RemoveTenantAsync(tenantId);              // dispose the tenant's bundle, at tenant removal
```

**Reads stay synchronous** — `InitializeTenantAsync` does the async work once; afterward the tenant snapshot is read synchronously, exactly like the global config. Global reads and existing single-tenant apps are unchanged. Async is confined to the dynamic-tenant materialization moment.

### 5. Consumption — explicit `…ForTenant(id)`, never injection

Tenant-scoped values are obtained by **passing the tenant id**, never by DI injection:

```csharp
var smtp  = mgr.GetConfigForTenant<SmtpSettings>(tenantId);          // sync
var live  = mgr.GetReactiveConfigForTenant<SmtpSettings>(tenantId);
var store = mgr.GetWritableStoreForTenant<SmtpSettings>(tenantId);    // per-tenant write facade
var flags = mgr.GetFeatureFlagsForTenant<BillingFlags>(tenantId);
var ents  = mgr.GetEntitlementsForTenant<PlanEntitlements>(tenantId);
```

**Tenant-scoped types/flags are NOT DI-injectable.** Injecting one into a long-lived (Singleton) consumer would be a captive-dependency bug — it would freeze one tenant forever, since the container cannot know the runtime tenant. The `ServiceRegistrationPlanner` therefore tags and **excludes** types whose every rule is `.TenantScoped()` from the normal DI plan. Global types remain injectable as today. A consuming service injects the `ConfigManager` / `ITenantConfigurationAccessor` and calls `GetFeatureFlagsForTenant(currentTenant)` — explicitly tenant-aware, which is the correct shape for multi-tenant code.

### 6. Fan-out — automatic via per-tenant subscriptions (v1)

Each tenant snapshot layers on the global base, so a change to the **global** base must propagate to initialized tenants.

**v1 (implemented):** because each tenant pipeline runs the full flat rule list (§3) with its **own** provider instances and **own** change subscriptions, a live global base source (file / observable / http) propagates to every initialized tenant **automatically** — each tenant's own base subscription fires its own debounced recompute, re-seeding from the new base and re-emitting on its own `IReactiveConfig<T>` (content-gated by the engine's reference-equality change detection: a tenant that masks the changed key with its own override does not emit). No cross-pipeline coordinator runs, and there is no inline cross-pipeline read to deadlock. Consistency is per-tenant eventual, as decided below. This is verified by `TenantFanOutTests`.

**Deferred (only relevant if seed-from-global lands):** if the base is ever shared rather than re-run per tenant (§3), tenants would no longer self-subscribe and an explicit **fan-out coordinator** becomes necessary — observing the global commit via `MasterBackplane.SnapshotStream` strictly **after** the global Publish and semaphore release (never inline), recomputing subscribed tenants and stale-marking idle ones. That coordinator is **not built** in v1 because the full-list-per-tenant model makes it unnecessary.

### 7. Reach across the library

The tenant dimension is unified by the factory + bundle:

- **Feature Flags / Entitlements** become tenant-aware **without a source-generator change**: the generated flag class already reads an injected `IReactiveConfig<TConfig>`; tenant-awareness means constructing it with the **tenant's** `IReactiveConfig`. `GetFeatureFlagsForTenant<TFlags>(id)` is a per-`(tenant, TFlags)` factory/cache over the existing generated class. The context-aware evaluator and the REST endpoints (`MapFeatureFlagEndpoints`) gain a tenant dimension (e.g. a route segment).
- **WritableStore** per tenant: reads fall out of the factory (`FromStore(BackendFor(tenant))`; file backend = a folder per tenant); writes go through a per-tenant `GetWritableStoreForTenant<T>(id)` facade pointing at the tenant's backend.
- **Secrets** are tenant-capable via folder mode (`kid` = tenant subfolder routes decryption); a tenant writes its encrypted envelope to its own backend, decrypted with its own cert. **Encryption-key publishing is per tenant too:** `GetCurrentKeyForTenant(tenantId)` / `MapTenantSecretEncryptionKey` return that tenant's **single current public key** — the newest cert in its subfolder (older certs stay decrypt-only for rotation) — resolved from `ITenantContext`, never a list and never another tenant's key. See [Publishing Encryption Keys](/guide/secrets/key-publishing).

### 8. No-DI core preserved

Tenant methods live on a **new** `ITenantConfigurationAccessor` that `ConfigManager` also implements; the existing `IConfigurationAccessor` stays byte-identical. The whole tenant lifecycle is explicit method calls — usable with zero DI container.

### Settled product decisions

- **Authoring:** one flat rule list + per-rule `.TenantScoped()` + `Tenant` on `IConfigurationAccessor` (§1); `ForEachTenant`/tiered-builder/`{tenant}`-token/top-level-`(c, tenant)` rejected (*Alternatives considered*).
- **Fan-out:** automatic via per-tenant subscriptions in v1 (§6); explicit coordinator deferred with seed-from-global.
- **Precedence:** two-layer `[global]++[tenant]`, tenant on top; a tenant's plan/license is a config **value/flag**, not a precedence tier.
- **Consistency:** per-tenant **eventual consistency** — a global change propagates tenant-by-tenant as each rebuild finishes (a deliberate relaxation of ADR-002's single-snapshot atomicity, which still holds *within* each tenant and within the global state).
- **Eviction:** explicit `RemoveTenantAsync` only — no idle-eviction or cap (the active-tenant set is host-bounded).

---

## Engine impact

| File / Area | Change | Kind |
|---|---|---|
| `Core/ConfigManager.cs` | Single-pipeline ownership → global bundle + `ConcurrentDictionary<tenantId, Lazy<Task<TenantPipeline>>>`; add `InitializeTenantAsync`/`EnsureTenantInitializedAsync`/`IsTenantInitialized`/`RemoveTenantAsync`/`GetConfigForTenant`/`GetReactiveConfigForTenant`; extend dispose | **Structural** — *done (b2)* |
| **NEW** `TenantPipeline` | Bundle of per-tenant engine/state/backplane/reactive/rules on the shared scope + frozen registry | **Structural / new** — *done (a/b2)* |
| ~~`TenantFanOutCoordinator`~~ | **Not built in v1** — full-list-per-tenant gives automatic fan-out (§6); coordinator only needed if seed-from-global lands | Deferred |
| `Core/ConfigurationEngine.cs` | seed-from-global recompute variant — **deferred** (§3); v1 re-runs the full list per tenant (correct, unoptimized) | Deferred |
| `Core/MasterBackplane.cs`, `ConfigurationState.cs`, `ConfigurationAccessor.cs` | Instantiated per tenant (no internal change); per-tenant accessor so the recompute-window fallback reads tenant JSON, not global | Additive |
| `Rules/ConfigRule.cs` (+ Fluent) | `.TenantScoped()` marker on the rule builder (AND-composed with any `When`); `Tenant` on the accessor | Additive |
| `DI/ServiceRegistrationPlanner.cs` | Tag/exclude types whose every rule is `.TenantScoped()` from the normal DI plan | Additive |
| `DI/ServiceDescriptorEmitter.cs` | (Only if/when ambient injection is ever wanted — currently **out of scope**, see §5) | — |
| Abstractions | New `ITenantConfigurationAccessor`; existing `IConfigurationAccessor` unchanged | Additive |
| Flags/Entitlements | `GetFeatureFlagsForTenant`/`GetEntitlementsForTenant` factory/cache (no generator change); tenant dimension on evaluator + REST endpoints | Additive |

**Net:** one structural change (`ConfigManager` ownership → per-tenant `TenantPipeline` bundles). Everything else is additive reuse of existing per-instance machinery — fan-out is automatic via each tenant's own subscriptions, with **no coordinator subsystem in v1** (§6; the coordinator is deferred with seed-from-global). No rewrite of the recompute/snapshot/reactive cores.

---

## Consequences

✅ Reuses the existing recompute/merge/reactive engine wholesale; the global pipeline and single-tenant apps are unchanged
✅ Reuses PR #47's overlay/merge primitives (`BuildBaseJson`, `MutableJsonMerge`) — that work is on-path for tenancy, not superseded
✅ Reads stay synchronous; async is confined to explicit tenant init
✅ No source-generator change for tenant-aware flags
✅ No-DI core preserved; tenant API additive on a new interface
✅ Captive-dependency class of bugs avoided by design (explicit `…ForTenant(id)` only)

⚠️ Structural rework of `ConfigManager`'s "one state per manager" ownership model (mechanical but not an additive extension; `ConfigManager` is sealed) — **done**: extracted into `TenantPipeline`, global path byte-identical
⚠️ Per-tenant eventual consistency (vs. ADR-002 global atomicity) — a global base change lands tenant-by-tenant. Tuples stay atomic *within* a pipeline; mixed-scope tuples ARE supported (each element is read from one pipeline's snapshot), and a tuple's global-only element read per tenant is just an eventual-consistent copy — not a tuple-internal skew.
⚠️ Resource use scales linearly with initialized tenants × base rules (each tenant re-runs the global base); acceptable for a host-bounded active-tenant set, and the seed-from-global optimization can reclaim it later without an API change
⚠️ Each tenant holds its own subscription to live base sources — for an SSE/HTTP base that is N connections to the config server; document and revisit with seed-from-global if it bites at scale

---

## Open questions (implementation-level)

- **Fan-out throttle at scale:** with full-list-per-tenant, a global base change fans out as one independent debounced recompute per initialized tenant; whether a cross-pipeline throttle is needed depends on tenant/subscriber counts. Becomes pressing only if seed-from-global lands (a single coordinator then drives all tenants).
- **Tuples across scopes:** **resolved — supported.** Each element is read from the relevant pipeline's atomic snapshot (global skips `.TenantScoped()` overlays; per-tenant is effective). A *global* tuple with a type whose every rule is `.TenantScoped()` throws (no global value) — read it per tenant. ("Scope" is a rule property, not a type property; the earlier "not supported" framing was wrong.)
- **Idle-read freshness contract:** moot in v1 — idle initialized tenants self-update via their own subscriptions, so a sync read is current. (Re-opens only with seed-from-global's stale-mark model.)

---

## Alternatives considered (authoring API)

All rejected in favor of *one flat rule list + per-rule `.TenantScoped()` + `Tenant` on the accessor* (§1):

- **`ForEachTenant((r, tenant) => ConfigRule[])` block** (this ADR's first draft) — a second nested rule surface and a `Func<RulesBuilder, string, …>` shape, duplicating the builder. Rejected: a whole parallel authoring path for what is one bit of metadata on a rule. The flat list with `.TenantScoped()` expresses the same precedence (position in the list) without a sub-builder.
- **Tiered builder** (`UseConfiguration` / `UseTenantConfiguration` / `WithNonNegotiable`, as in the POC) — makes precedence three named tiers. Rejected: "non-negotiable" is just a global rule placed after the tenant overlay in the flat list (§3), so the third tier is redundant; the two extra entry points add API surface without new capability.
- **`{tenant}` path token** (magic string interpolated by providers) — rejected: pushes tenancy into every provider's option parsing, exactly the "tenant-aware provider" coupling §1 avoids. The existing `Func<IConfigurationAccessor, T>` factory already interpolates `a.Tenant` with no provider change.
- **Top-level `(c, tenant)` lambda** on `AddCocoarConfiguration` — rejected: forces the tenant id into the *definition* phase, whereas the id is a *query-time* value (§5); it also can't express "global base + tenant overlay for the same type" in one list.

---

## References

- PR #47 — WritableStore sparse override overlay (`ConfigManager.BuildBaseJson`, `MutableJsonMerge`) — the merge/overlay foundation reused here
- `src/Cocoar.Configuration/Core/ConfigurationEngine.cs` — recompute pipeline (per-instance semaphore + scheduler)
- `src/Cocoar.Configuration/Core/MasterBackplane.cs` — `SnapshotStream` (fan-out hook), per-instance publish/dispose
- `src/Cocoar.Configuration/Core/ConfigManager.cs` — current single-pipeline ownership to be extended
- ADR-002 — atomic reactive updates (relaxed to per-tenant eventual consistency here)
- ADR-004 — aggregate rules (`AggregateConfigRule` precedent for grouping sub-rules)
