# Feature Flags & Entitlements — Philosophy, Vision, and Roadmap

This document explains *why* the feature flags and entitlements system is designed the way it is, what it is intended to become, and what principles should guide future development. It is the document to read before asking "why isn't this done differently?" and before making any significant changes to the design.

---

## The Central Insight

> **Feature flags and entitlements are config evaluation, not config properties.**

Most teams treat feature flags as boolean properties somewhere in a config file or a remote service. This library treats them as **decisions derived from configuration**, evaluated in context, at call time.

The distinction matters:
- A config property is a value. You read it.
- A flag is a question. You ask it.

`flags.NewCheckoutEnabled()` is not reading a value. It is asking: *"given the current state of the world, should the new checkout run right now?"* The answer is derived from configuration — but the question, its expiry, and its meaning are defined in code.

---

## Why Flags Are Defined in Code — Intentionally

This is the most frequently questioned design decision, so it gets its own section.

**Flags cannot be created or modified without a code change. This is by design.**

A common objection is: "other flag services let you create flags without a deployment." This is only partially true, and it is worth being precise about what those services actually do and do not eliminate.

Every feature flag library — LaunchDarkly, Unleash, Flagsmith, Microsoft.FeatureManagement — requires code that checks the flag:

```csharp
// LaunchDarkly
if (ldClient.BoolVariation("new-checkout", user, false)) { ... }

// Microsoft.FeatureManagement
if (await featureManager.IsEnabledAsync("NewCheckout")) { ... }
```

That check must exist in code before the dashboard flag does anything at all. If you create a flag in LaunchDarkly's dashboard but never write the check, nothing in your application changes. The dashboard gives you the ability to change the *value* of `"new-checkout"` at runtime. It does not eliminate the need for a PR that adds the check. The PR still happens — it just happens under a magic string that the compiler cannot verify, that IntelliSense cannot navigate, and that nobody will notice is dead code two years from now.

In those libraries, a product manager can create flag *definitions* in the dashboard without engineering. But the code that reacts to those definitions still requires a deployment. The deployment is not eliminated — it is just decoupled in time and invisible in the diff.

This library makes that PR explicit and gives it a type, a name, an expiry date, and an owner. The code that checks the flag IS the flag definition. There is one thing to write, one thing to review, one thing to delete.

In most flag services (LaunchDarkly, Unleash, etc.), nothing stops a product manager from creating flags in a dashboard with no engineering involvement, no expiry date, and no owner. Flags accumulate silently. Years later the codebase is full of `BoolVariation("old-checkout-v3", ...)` calls that nobody dares remove because nobody knows if the flag is still in use and what happens if it is toggled.

### Code as the Source of Truth — ConfigHub as a View

In other flag services the dashboard and the codebase are two separate lists that must be kept in sync manually:

- A flag can exist in the dashboard but have no check in code — it does nothing.
- A check can exist in code against a string that was never created in the dashboard — it silently falls back to the default.
- Neither side knows what the other contains.

This library inverts that relationship. **The code is the single source of truth. ConfigHub is a view of it.**

When `AppFeatureFlags` is defined, the registry knows about it immediately on startup. When ConfigHub connects, it reads the registry and sees exactly what exists in the application — not what someone remembered to create in a UI, not what a developer once typed as a string that the compiler cannot verify. What the compiler knows about. The two lists are always identical because there is only one list.

```
Other libraries:
  Dashboard has:   new-checkout, old-checkout-v2, experiment-42
  Code checks for: new-checkout, old-checkout-v3
  → Nobody knows which combinations are live, dead, or mismatched.

This library:
  Registry has:    NewCheckoutEnabled, BetaCheckoutEnabled, AdvancedAnalyticsEnabled
  ConfigHub shows: NewCheckoutEnabled, BetaCheckoutEnabled, AdvancedAnalyticsEnabled
  → Always identical. By construction.
```

Expiry information travels with it automatically. ConfigHub does not just show what flags exist — it shows which are expiring soon, which are already past their deadline, which need attention. Nobody has to remember to set a "stale" marker in a dashboard. It comes from the code that owns the flag.

This library takes a different stance: **adding a feature flag is an engineering act, and it should be treated as one.**

The consequences of this decision:

**The date is a contract, not a reminder.** Every `FeatureFlags` class requires `ExpiresAt`. This is a contract between engineering and product: "this code will be temporary, and we agree on when it needs to be cleaned up." The product manager has to give a date — or the developer has to choose one and get it agreed. Neither can happen silently.

**Extending a flag requires a PR.** If a flag's deadline passes and the feature isn't ready, you cannot just move the date in ConfigHub. You have to open a pull request, change the date in code, get it reviewed. This creates a paper trail and forces the conversation: "why isn't this done yet?"

**Promoting a flag to an entitlement is explicit.** When temporary feature work becomes a permanent business rule, it cannot happen quietly. It requires changing from a `FeatureFlags` subclass to an `Entitlements` subclass — a conscious act, visible in the diff, that says: "this is no longer temporary, this is a business rule that will live permanently."

**The TypeScript client makes this visible to frontend engineers.** A frontend developer looking at `Flag<bool> NewCheckoutEnabled` with an expiry date immediately understands: this component will change, it is temporary, do not build permanent UX around it. The type communicates intent without documentation.

The actual limitation is narrower than it first appears. You cannot define a *new named concept* without code — a new `Flag<bool>` with new evaluation logic requires a PR. But operational kill switches do not need new concepts. Consider an ASP.NET Core filter written once that reads a config property (`string[] BlockedRoutes`, or a regex pattern) and gates requests at runtime. The filter logic is in code; the data driving it — which routes to block — is in config and can be changed via ConfigHub with no deployment. That is the same pattern the entire library is built on.

What is genuinely missing is a **built-in** generic mechanism for this. The library does not provide a ready-made kill switch filter, and more importantly, there is no equivalent for non-endpoint code — service methods, background jobs, domain logic. For those, an explicit check must exist somewhere in code. Whether that check is a specific named flag or a generic "is this feature disabled?" config lookup is a decision for the developer writing that code.

---

## Feature Flags vs Entitlements — An Organizational Distinction

The two types look technically similar. They are not the same thing.

| | Feature Flag | Entitlement |
|---|---|---|
| **Core question** | "Does this code run?" | "May this actor do this?" |
| **Ownership** | Engineering / Ops | Product / Business |
| **Lifetime** | Temporary — MUST expire | Permanent — no expiry |
| **Explains to** | DevOps, SRE | Product, Sales, Customers |
| **Removal** | Dead code removal | Only if the business rule changes |

The litmus test: **a feature flag without an expiry date is an entitlement in disguise.** If you find yourself wanting to remove `ExpiresAt` from a flag, that is a signal that the concept you are modelling is a permanent business rule, not a temporary toggle. Use `Entitlements` instead.

This is not a technical distinction. The underlying mechanism is identical: both read from `IReactiveConfig<T>` and evaluate at call time. The distinction is organizational — it forces people to be explicit about what they are modelling.

---

## Flags Are Config Evaluation

No new providers are needed. No new storage. No new delivery mechanism.

```
Config sources (file, environment, HTTP, SignalR, ...)
    ↓
ConfigManager (rules, layering, atomic updates)
    ↓
IReactiveConfig<T>.CurrentValue   ← always the latest atomic snapshot
    ↓
FeatureFlags / Entitlements       ← re-evaluate on every call
    ↓
Application code
```

This means:
- Changing a config value changes flag behavior. No deployment.
- Changing config sources changes where flag data comes from. No flag-specific infrastructure.
- Atomic multi-config updates (tuple reactive configs) mean a flag that reads from two config types cannot observe a half-updated state.

### Targeting Is Config + Context

The common objection is: "real feature flag services have targeting rules — roll out to 10% of users, enable for specific user IDs, etc." This library does not have a targeting rule engine, and it does not need one.

A contextual flag `Flag<UserContext, bool>` receives context at evaluation time. The targeting logic is written in C#:

```csharp
EnabledForUser = DefineFlag<UserContext, bool>(
    nameof(EnabledForUser),
    user => _config.CurrentValue.BetaUserIds.Contains(user.Id)
);
```

The *logic* is in code. The *data* (`BetaUserIds`) is in config. To change who gets the feature, change the config — via ConfigHub, a file edit, an environment variable, whatever source the rule is reading from. No redeployment required.

This approach is more flexible than a fixed rule engine because the C# lambda can express any logic: percentile bucketing, geographic rules, plan-based access, tenant features. It is more maintainable because the logic is readable, type-safe, and testable. The tradeoff is that the *shape* of the targeting logic cannot change without a deployment — but in practice, the shape rarely needs to change once the flag is written. What changes is the data.

---

## The Reactivity Model

Flags have no internal state and no subscriptions. Every call re-reads `IReactiveConfig<T>.CurrentValue`. This means:

- No cache to invalidate
- No stale values after a config update
- No setup beyond injecting `IReactiveConfig<T>`

The `IReactiveConfig<T>` always holds the most recent atomically committed snapshot from the last successful recompute. If the config changes (file modified, HTTP poll returns new data, SignalR push arrives), the next call to any flag automatically reflects the new state.

For flags that cross config type boundaries, use tuple reactive configs to guarantee atomicity:

```csharp
IReactiveConfig<(BillingConfig Billing, PlanConfig Plan)> _config;

PremiumEnabled = DefineFlag(
    nameof(PremiumEnabled),
    () => _config.CurrentValue.Billing.NewFlowEnabled &&
          _config.CurrentValue.Plan.Tier == "premium"
);
```

Both values come from the same atomic snapshot. They cannot be from different recompute cycles.

---

## The Planned Ecosystem

The flag library as it exists today is the foundation layer. The full vision requires several more components.

### ConfigHub

A web UI and backend service for managing configuration and flags at runtime. Key properties:

- **Self-hostable** — runs in your own infrastructure
- **SaaS option** — hosted ConfigHub for teams that do not want to operate it
- **Real-time delivery** — changes pushed via SignalR to all connected applications immediately
- **Flag inventory** — reads the registries to display all flags in the application, their expiry, their current evaluation, their source config
- **Plan hierarchy** — a UI concept for defining entitlement tiers, which translates to config values that the `Entitlements` classes already know how to read

ConfigHub does not store configuration in its own database as the source of truth. It is a management interface over the same config sources that already exist (files, environment, HTTP). The SignalR provider becomes one more source in the layered config system.

**What ConfigHub can change:** config values — the data that flags evaluate against.
**What ConfigHub cannot change:** flag definitions, expiry dates, evaluation logic. Those require code.

This is the intentional boundary. Runtime management of values: yes. Runtime management of what a flag means: no.

### Flag Lifecycle in ConfigHub

When a new version of an application connects, ConfigHub updates its view from the registry. Flags that are new appear. Flags that were removed are **not deleted** — they are marked as "no longer available." This is intentional:

- If a flag disappears unexpectedly (bad deploy, wrong branch deployed), it is immediately visible rather than silently absent.
- Config values that were set for the flag are retained as a historical record.
- The audit trail is preserved: the flag existed, was active from date X to date Y, and was removed in deployment Z.

The full lifecycle:

```
Code defines flag        →  ConfigHub: Active
Flag expires (IsExpired) →  ConfigHub: Degraded / cleanup needed
Flag removed from code   →  ConfigHub: No Longer Available (history preserved)
Explicitly archived      →  ConfigHub: Archived
```

Nothing disappears silently. Every state transition is meaningful and visible.

**Renames and flag identity:** Renaming a flag class or property in code has no impact on stored state — flags have no stored values anywhere. ConfigHub updates the label in the dependency visualization and the TypeScript client API changes, but there is no migration needed. The flag identity problem does not exist because flags have nothing to migrate.

**Config property identity across renames:** Config *does* have stored values in ConfigHub. Renaming a config property, moving it to a different config type, or renaming a config type entirely creates an orphaned stored value. This is a broader config management problem that affects every config type managed through ConfigHub.

The planned approach:

- **Nothing is ever silently deleted.** An orphaned key (present in ConfigHub but no longer in code) is marked as such and remains visible in the UI with its last known value intact.
- **A configurable mapping table** allows developers to declare that old key X is now key Y. When configured, ConfigHub migrates the value automatically and archives the old key with a note.
- **Without a mapping configured**, the orphaned key and the new keyless property are both visible side by side in the UI. The developer can copy the value manually. Tedious, but nothing is lost and nothing is silently wrong.

Automatic rename detection (inferring intent from a diff) is deliberately avoided — it is too easy to confuse a rename with an unrelated delete-and-add. Explicit mapping keeps intent clear and migration auditable.

### SignalR Config Provider

A new provider (`rule.For<FeatureConfig>().FromConfigHub(...)`) that connects to ConfigHub and receives config updates over SignalR in real time. Because flags re-read `CurrentValue` on every call, a push from ConfigHub flows through without any flag-specific wiring:

```
ConfigHub push → SignalR provider → recompute → IReactiveConfig<T> updated → next flag call returns new value
```

### Evaluation Tracking and Telemetry

Currently, flag evaluations are invisible. The plan is to surface:

- How often each flag is evaluated
- What values it returns
- Which config types and rules feed into each flag (dependency graph: "flag X reads from config Y which comes from rule Z")
- History of when values changed and why

This will be part of the Telemetry system (a rename from "Health" — health implies binary up/down, telemetry captures the full operational picture). The dependency tracking in particular is novel: it will allow ConfigHub to show "if I change this config value, these flags will be affected."

### Client Libraries

For frontend applications (browser, mobile), the consumption model is different from server-side. Config types cannot be deserialized on the client — they are a server concept. But flags have a simple interface: given a context, return a value.

The planned client approach:
- A REST endpoint for one-shot flag evaluation: `POST /flags/evaluate` with context, returns flag values
- A SignalR client for reactive flag updates: subscribe to flag changes with context, get pushed updates when values change

The TypeScript SDK will expose an API mirroring the server-side design — you define which flags you care about and what context you provide, and the SDK handles the transport.

**Why this is separate from config serialization:** config classes are server constructs. The client should never need to know that `NewCheckoutEnabled` comes from a `FeatureConfig` JSON object. The client asks "is new checkout enabled for this user?" and gets true or false. The server handles the rest.

### Enforcement Integration

Currently, entitlements must be checked manually at every call site. The plan is to provide optional enforcement layers for server-side scenarios:

- ASP.NET Core policy integration: `[Authorize(Policy = "CanExportData")]` that resolves against the registered `Entitlements` class
- Minimal API endpoint extension: `.RequiresEntitlement<AppPlanEntitlements>(e => e.CanExportData)`

These are additive — the delegate-based API remains the primary pattern. The enforcement layer is for cases where you want the framework to handle the check rather than doing it in every handler.

---

## What Is Done vs Planned

### Done

| Area | Status |
|------|--------|
| `FeatureFlags` base class with `ExpiresAt`, `IsExpired`, metadata | ✅ Implemented |
| `Entitlements` base class, permanent, no expiry | ✅ Implemented |
| `Flag<T>`, `Flag<TContext, TResult>` delegate types | ✅ Implemented |
| `Entitlement<T>`, `Entitlement<TContext, TResult>` delegate types | ✅ Implemented |
| Per-flag metadata (name, description, expiry override) | ✅ Implemented |
| `IFeatureFlagsRegistry` + `IEntitlementsRegistry` | ✅ Implemented |
| `UseFeatureFlags` / `UseEntitlements` builder extensions | ✅ Implemented |
| DI auto-registration of registries and flag classes | ✅ Implemented |
| Health integration — expired flags → `Degraded` status | ✅ Implemented |
| Showcase in `ConfigurationShowcase` Blazor app | ✅ Implemented |

### Planned

| Area | Notes |
|------|-------|
| ConfigHub web UI | Self-hosted + SaaS, manages config values at runtime |
| SignalR config provider | Real-time config delivery from ConfigHub |
| Evaluation tracking / telemetry | Per-flag call counts, value history, dependency graphs |
| Health → Telemetry rename | Broader than up/down status |
| TypeScript client | REST + SignalR, evaluates flags with context on the server |
| ASP.NET Core enforcement | Policy integration for entitlements |
| Flag registry keying by JSON path | Hierarchical namespacing for large apps |
| Plan hierarchy in ConfigHub | UI concept for entitlement tier management |

---

## Principles for Future Developers

**Do not add a way to create flags without an expiry.** The forced expiry is not a bug to work around. It is the mechanism by which the library enforces organizational discipline. If someone wants to create a permanent toggle, the answer is `Entitlements`.

**Do not make the config values owned by the flag library.** Flags evaluate config; they do not store it. The storage is always a config provider. This keeps the flag layer thin and avoids a second source of truth.

**Do not build a rule engine into flag evaluation.** The `Flag<TContext, TResult>` delegate is the targeting mechanism. The logic is C#. Adding a DSL or rule builder would add complexity and limit what targeting logic can express. If the data driving targeting needs to change at runtime, that is a config change — served by the existing config system.

**The boundary between what ConfigHub can change and what requires code is intentional.** Config values: ConfigHub. Flag definitions, expiry, and evaluation logic: code. Do not blur this boundary. The organizational value of the library depends on it.

**Entitlements are permanent until the business rule changes.** Do not add expiry to entitlements. If a business rule that was permanent turns out to be temporary, it should have been a feature flag. The lack of expiry on `Entitlements` is a statement, not an oversight.

---

## Summary

The feature flags and entitlements system is built on three ideas:

1. **Flags are config evaluation.** No new storage, no new providers. Change the config, change the flag behavior. The reactive config system handles delivery.

2. **Flags must be defined in code.** This is a deliberate constraint that encodes organizational process into the type system. Expiry dates are contracts. Promotions to entitlements are explicit. Dead code stays visible until someone removes it.

3. **Feature flags and entitlements are organizationally different things.** The type system enforces this. The distinction forces conversations that most teams avoid: "is this temporary or permanent?" "who owns this?" "when does this go away?"

ConfigHub, the SignalR provider, the TypeScript SDK, and evaluation telemetry are the next layers. They extend the system's reach without changing its principles.
