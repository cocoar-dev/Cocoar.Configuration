---
name: cocoar-configuration
description: >
  Reactive, strongly-typed configuration for .NET with Cocoar.Configuration. Use when working
  with ConfigManager, AddCocoarConfiguration, UseConfiguration rule lists
  (rule.For<T>().FromFile/FromEnvironment/FromHttp/FromConfigHub/FromStore ...), Required() and
  optional rules, config-aware or conditional rules, IReactiveConfig<T> and reactive tuples,
  Secret<T> and X.509 secret encryption, IFeatureFlags<TConfig> / IEntitlements<TConfig> classes
  and their REST endpoints, multi-tenant configuration (.TenantScoped(), ...ForTenant),
  service-backed configuration, the WritableStore, configuration health checks,
  CocoarTestConfiguration test overrides, the COCFG/COCFLAG analyzer diagnostics, or a migration
  from IOptions<T> / IConfiguration.
metadata:
  author: Bernhard Windisch
  source: https://docs.cocoar.dev/configuration/
---

# Cocoar.Configuration

Cocoar.Configuration loads plain C# classes from layered sources — files, environment, command
line, HTTP, ConfigHub, a writable store — merges them at the JSON level, and keeps them current:
when a source changes, the whole configuration is recomputed and committed atomically, and
subscribers get the new snapshot. Feature flags, entitlements, encrypted secrets, multi-tenancy
and health monitoring build on the same pipeline. This skill is the documentation, page by page,
under `references/`. The index at the bottom says which page answers what.

## Packages

| Package | Purpose |
|---|---|
| `Cocoar.Configuration.AspNetCore` | ASP.NET Core apps: includes DI and core; health checks, flag/entitlement REST endpoints, encryption-key endpoints |
| `Cocoar.Configuration.DI` | `AddCocoarConfiguration()` for Microsoft.Extensions.DependencyInjection without ASP.NET Core; service-backed configuration |
| `Cocoar.Configuration` | Core: `ConfigManager`, built-in providers (JSON file, dotenv, INI, environment, command line, static, observable, writable store), reactive engine, secrets, flags, entitlements; brings the analyzers |
| `Cocoar.Configuration.Abstractions` | Interfaces only: `IConfigurationAccessor`, `IReactiveConfig<T>`, `ISecret<T>`, `SecretLease<T>` |
| `Cocoar.Configuration.ConfigHub` | `FromConfigHub()` — authenticated snapshots with SSE invalidation |
| `Cocoar.Configuration.Http` | `FromHttp()` — one-time fetch, polling, Server-Sent Events |
| `Cocoar.Configuration.MicrosoftAdapter` | `FromIConfiguration()` — bridge existing `IConfiguration` sources |
| `Cocoar.Configuration.Yaml`, `Cocoar.Configuration.Toml` | `FromYamlFile()`, `FromTomlFile()` |
| `Cocoar.Configuration.WritableStore.Marten` | `FromMartenStore()` — Marten/PostgreSQL backend for the writable store, database-per-tenant aware |
| `Cocoar.Configuration.Secrets.Cli` | Global .NET tool: encrypt/decrypt secrets in config files, generate and convert certificates |

Install the highest of AspNetCore → DI → core that fits; each includes the ones below it.

## Things an assistant gets wrong without the docs

- **This is not `IOptions<T>`.** Configuration classes are injected directly (`AppSettings`),
  with no wrapper, no `Configure<T>` and no section binding. Live values come from
  `IReactiveConfig<T>` (`CurrentValue`, `Subscribe`), not `IOptionsMonitor<T>`.
- **A type with a rule is already registered in DI.** Do not add `setup.ConcreteType<T>()` just
  to register it; the setup lambda is for exposing interfaces, changing lifetimes or mapping
  interface-typed properties.
- **Configuration types are Scoped, `IReactiveConfig<T>` is Singleton — on purpose.** Resolving a
  config type is a dictionary lookup, not a recompute. Making it Singleton does not make it
  faster; it freezes the consumer on the instance from startup. A Singleton service that needs
  current values takes `IReactiveConfig<T>`.
- **Rules run top to bottom and merge property by property; the last rule that sets a property
  wins.** A later rule overrides only what it contains. Order matters within one type, and a rule
  that reads another type through the accessor must come after that type's rules (COCFG002).
- **Rules are optional by default.** A failing provider contributes `{}`: earlier values stay,
  the type falls back to its C# defaults, health turns `Degraded`. Only `.Required()` makes a
  failure fatal — an exception at startup, a full rollback to the last good state at runtime.
- **`ConfigManager` has no public constructor.** Create it with `ConfigManager.Create(c => ...)`
  or `CreateAsync`; in DI apps `AddCocoarConfiguration(c => ...)` does it.
- **Configuration types must be classes** — `For<T>()` is constrained to reference types, so a
  struct does not compile.
- **File paths resolve against `AppContext.BaseDirectory`**, not the current working directory.
- **Change detection is reference equality.** Each recompute that changes the data produces a
  new instance; unchanged data keeps the same instance and does not notify. Never mutate a
  configuration object.
- **Several configs that must change together are one tuple**: `IReactiveConfig<(A, B)>` updates
  atomically. Two separate subscriptions can observe a mixed state.
- **A `Secret<T>` is read through a lease**: `using var lease = secret.Open();` then
  `lease.Value`. The lease zeroes the decrypted bytes on dispose — do not copy the value into a
  field or log it. Secrets need `UseSecretsSetup(...)`; `AllowPlaintext()` is for development and
  tests.
- **Flag and entitlement classes are `partial` and have no hand-written constructor.** The
  source generator emits the constructor and the `Config` property from
  `IFeatureFlags<TConfig>` / `IEntitlements<TConfig>`; flags also need `ExpiresAt`. Members are
  `FeatureFlag<...>` / `Entitlement<...>` delegates — pure functions of `Config` and an optional
  context; side effects belong in context resolvers. Register them with
  `UseFeatureFlags(f => [f.Register<T>()])`.
- **Tenant configuration is read explicitly.** Mark per-tenant rules with `.TenantScoped()`, read
  the tenant id as `accessor.Tenant` in rule factories, and consume with
  `GetConfigForTenant<T>(id)` and its siblings. There is no ambient tenant.
- **Providers that need DI services go into `UseServiceBackedConfiguration`**, not
  `UseConfiguration`: the first layer is built before the container exists.
- **`System.Reactive` is not a dependency.** `IReactiveConfig<T>` is a plain `IObservable<T>`; do
  not assume Rx operators are available unless the consuming project references Rx itself.
- **Tests override configuration with `CocoarTestConfiguration`** (`ReplaceConfiguration`,
  `AppendConfiguration`, `ReplaceSecretsSetup`), which is `AsyncLocal`-isolated and parallel-safe
  — not by writing temp files or setting environment variables.
