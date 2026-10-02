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

<!-- Everything below is generated by website/scripts/sync-skill.mjs from the docs frontmatter. Edit the docs, not this file. -->

## Reference documentation

Each file under `references/` is one page of the documentation, copied verbatim. Read the one
whose description matches the task; they are independent of each other.

### Introduction

- [Getting Started](references/guide/getting-started.md) — Install Cocoar.Configuration packages, define a POCO config class, wire FromFile rules in ASP.NET Core, console, and DI setups
- [Working with Certificates](references/guide/certificates.md) — X.509 certificates in Cocoar — why password-less, protecting PFX via file permissions on Linux/macOS/Windows and Docker/Kubernetes

### Configuration

- [Rules & Layering](references/guide/configuration/rules.md) — Rule anatomy with For<T>().FromFile, top-to-bottom property-by-property JSON merge layering, last-write-wins, and Select to extract a sub-document
- [Required vs Optional Rules](references/guide/configuration/required-optional.md) — Optional rules degrade gracefully to empty {} with Degraded health, Required() rolls back the recompute on failure with Unhealthy status and startup exception
- [Setup & Type Exposure](references/guide/configuration/setup.md) — Auto-registration of rule types as Scoped, the setup lambda with ConcreteType<T>().ExposeAs<I>(), Interface<I>().DeserializeTo<T>(), lifetimes, disabling auto-registration
- [Config-Aware Rules](references/guide/configuration/config-aware.md) — Rules that read earlier results via IConfigurationAccessor (GetConfig/TryGetConfig) to derive dynamic file paths and HTTP endpoints, with COCFG002 order enforcement
- [Conditional Rules](references/guide/configuration/conditional-rules.md) — Conditionally enable rules with .When(accessor) over earlier config state, Skipped health status, dynamic source selection, COCFG002 rule-order checking
- [Aggregate Rules](references/guide/configuration/aggregate-rules.md) — Group sub-rules into one unit with FromFiles file-layering shorthand and Aggregate() over mixed providers, aggregate-level vs sub-rule Required semantics

### Providers

- [Providers Overview](references/guide/providers/overview.md) — Provider contract (FetchConfigurationBytesAsync, ChangesAsBytes), built-in providers, key-based instance caching, provider vs query options, lifecycle
- [File Provider](references/guide/providers/file.md) — FromFile JSON provider, directory file watcher, AppContext.BaseDirectory path resolution, debouncing, path-traversal protection, Kubernetes ConfigMap symlink support (followSymlinks), optional vs Required, dynamic paths
- [YAML Provider](references/guide/providers/yaml.md) — FromYamlFile provider (Cocoar.Configuration.Yaml) — reactive .yaml/.yml watching, YAML core-schema scalar type-inference (bool/number/null), quoted/block scalars stay strings
- [TOML Provider](references/guide/providers/toml.md) — FromTomlFile provider (Cocoar.Configuration.Toml) — reactive .toml watching, TOML typed values (string/int/float/bool/datetime/array/table) mapped to JSON, arrays-of-tables, Kubernetes ConfigMap support
- [Dotenv (.env) Provider](references/guide/providers/dotenv.md) — FromDotEnv provider (core, no dependency) — .env KEY=value parsing, # comments, export prefix, single/double quotes, inline comments, :/__ key nesting, reactive file-watching
- [INI Provider](references/guide/providers/ini.md) — FromIniFile provider (core, no dependency) — .ini [section] headers, key=value, ;/# whole-line comments, :/. nesting, quote stripping, connection-string-safe (no inline-comment stripping), reactive watching
- [Environment Variables Provider](references/guide/providers/environment.md) — FromEnvironment provider, case-insensitive prefix filtering, __ and : nesting, indexed collections, final-override pattern, dynamic per-tenant prefix
- [Command Line Provider](references/guide/providers/command-line.md) — FromCommandLine provider, switch-prefix filtering, key=value/key value/boolean-flag formats, : and __ nesting, custom prefixes, highest-priority override
- [ConfigHub Provider](references/guide/providers/confighub.md) — Cocoar.Configuration.ConfigHub provider — authenticated snapshots, ETag validation, SSE invalidation, reconnect reconciliation, polling fallback, dynamic endpoints, and reported dimensions with warning handling
- [HTTP Provider](references/guide/providers/http-polling.md) — FromHttp provider — one-time fetch, polling, SSE, SSE-with-fallback, failure threshold, dynamic endpoints, client-certificate and encrypted-secret token auth
- [Microsoft IConfiguration Adapter](references/guide/providers/microsoft-adapter.md) — FromIConfiguration adapter bridging Microsoft IConfiguration, colon-key flattening to nested JSON, .Select section filtering, GetReloadToken change detection, gradual migration
- [Static & Observable Providers](references/guide/providers/static-observable.md) — FromStaticJson/FromStatic fixed-value providers and FromObservable wrapping IObservable<T> or IObservable<string>, BehaviorSubject for WebSocket/gRPC/queue/test updates
- [Writable Store Provider](references/guide/providers/writable-store.md) — FromStore writable override layer, sparse leaf persistence, IWritableStore<T> SetAsync/ResetAsync/PatchAsync, reset vs explicit null, DescribeAsync provenance, secrets, IStoreBackend
- [Marten Store](references/guide/providers/marten-store.md) — Marten/PostgreSQL writable-store backend, FromMartenStore service-backed rule, database-per-tenant via .TenantScoped, CocoarConfigDocument storage model, single-process reactivity and HA notes
- [Building Custom Providers](references/guide/providers/custom.md) — Extend ConfigurationProvider<TOptions,TQuery>, FetchConfigurationBytesAsync/ChangesAsBytes, GenerateProviderKey caching, fluent FromProvider extension, service-backed DI providers, change detection, secret envelopes

### Dependency injection

- [DI Setup](references/guide/di/setup.md) — AddCocoarConfiguration for Microsoft.Extensions.DI — auto-registration, ConcreteType/ExposeAs/Interface DeserializeTo, DisableAutoRegistration, flags, secrets
- [ASP.NET Core Integration](references/guide/di/aspnetcore.md) — Cocoar.Configuration.AspNetCore — WebApplicationBuilder.AddCocoarConfiguration, health endpoint, feature flag and entitlement REST endpoints, injecting config
- [Lifetimes & Registration](references/guide/di/lifetimes.md) — DI lifetimes — Scoped config types, Singleton IReactiveConfig<T>, AsSingleton/AsTransient/AsScoped, keyed services, exposed-type lifetimes, deterministic ordering
- [Service-Backed Configuration](references/guide/di/service-backed.md) — Two-layer DI-aware config (ADR-006) — UseServiceBackedConfiguration with (sp,a) factories, FromHttp via IHttpClientFactory, FromStore, FromService, host-start activation

### Reactive updates

- [IReactiveConfig\<T\>](references/guide/reactive/basics.md) — IReactiveConfig<T> : IObservable<T> — CurrentValue, Subscribe, replay-1 BehaviorSubject semantics, reference-equality change detection, atomic swap, Scoped vs Singleton
- [Reactive Tuples](references/guide/reactive/tuples.md) — IReactiveConfig<(T1, T2)> for atomic multi-config updates — same-snapshot guarantee, per-element change detection, 2–8+ arities, automatic DI registration
- [Debouncing](references/guide/reactive/debouncing.md) — Trailing-edge debounce coalescing rapid source changes — 300ms default, UseDebounce config, cross-provider coalescing, recompute-from-earliest-changed-rule, during-run changes

### Feature flags & entitlements

- [Feature Flags vs Entitlements](references/guide/flags/concepts.md) — Feature flags vs entitlements as pure functions over config, FeatureFlag<T>/Entitlement<T> delegates, ExpiresAt health signal, the litmus test, Cocoar vs LaunchDarkly
- [Defining Feature Flags](references/guide/flags/defining-flags.md) — Defining IFeatureFlags<TConfig> partial classes, FeatureFlag<TResult> and FeatureFlag<TContext,TResult> delegates, tuple multi-config, ExpiresAt, source-generated Config property
- [Defining Entitlements](references/guide/flags/defining-entitlements.md) — Defining IEntitlements<TConfig> partial classes, Entitlement<TResult> and Entitlement<TContext,TResult> delegates, tuple multi-config, permanent business logic with no ExpiresAt
- [Registration](references/guide/flags/registration.md) — Registering flags/entitlements via UseFeatureFlags/UseEntitlements, Register<T>, global/class/property-level resolvers, priority cascade, Core-only no-DI overload
- [Context Resolvers](references/guide/flags/context-resolvers.md) — IContextResolver<TRequest,TContext> hydrating request DTOs into domain context, global/class/property registration levels, Scoped lifetime, evaluation pipeline for contextual flags
- [REST Evaluation Endpoints](references/guide/flags/rest-endpoints.md) — MapFeatureFlagEndpoints/MapEntitlementEndpoints GET/POST routes, custom path prefixes, RequireAuthorization and middleware chaining, error status codes, resolver-backed POST evaluation
- [Expiry & Health](references/guide/flags/expiry-health.md) — Flag ExpiresAt lifecycle, Degraded health when expired, IFeatureFlagsDescriptors.All/Expired, health endpoint integration, compile-time static-date validation, define-to-cleanup lifecycle

### Multi-tenancy

- [Multi-Tenancy](references/guide/multi-tenancy/overview.md) — Per-tenant pipeline bundles on a shared global base, .TenantScoped() rules, accessor.Tenant, …ForTenant reads, scoped ITenantReactiveConfig, per-tenant flags/secrets/WritableStore, global fan-out

### Secrets

- [Secrets Overview](references/guide/secrets/overview.md) — Built-in encrypted-at-rest secrets via X.509 certificates and cocoar.secret envelopes, Secret<T> properties, lease-based decrypted access with memory zeroing
- [Secret\<T\> & Leases](references/guide/secrets/secret-type.md) — Declaring Secret<T> and ISecret<T> properties for strings, byte arrays and numbers, Open() leases and SecretLease<T> that zero decrypted bytes on dispose
- [Encryption Setup](references/guide/secrets/encryption-setup.md) — UseSecretsSetup with X.509 hybrid encryption (RSA-OAEP + AES-256-GCM), UseCertificateFromFile/WithKeyId single-cert, PFX/PEM formats, certificate-folder mode
- [Publishing Encryption Keys](references/guide/secrets/key-publishing.md) — Exposing public keys via MapSecretEncryptionKey and MapTenantSecretEncryptionKey on /.well-known/cocoar/encryption-key, single- vs multi-tenant, ITenantContext, response shape
- [Browser & Client Encryption](references/guide/secrets/client-encryption.md) — @cocoar/secrets TypeScript library — fetchEncryptionKey and encryptSecret build cocoar.secret envelopes client-side via WebCrypto so plaintext never reaches the server, multi-tenant
- [CLI Tools](references/guide/secrets/cli.md) — cocoar-secrets global .NET tool — encrypt values to JSON envelopes (incl. from stdin), generate self-signed certs, convert password-protected PFX to password-less
- [Certificate Caching](references/guide/secrets/certificate-caching.md) — UseCertificatesFromFolder time-limited private-key caching (cacheDurationSeconds), FileSystemWatcher auto-discovery, two-level cache, zero-downtime certificate rotation
- [Security Model](references/guide/secrets/security-model.md) — Memory-safety guarantees of the lease pattern (Array.Clear/ZeroMemory, stackalloc keys), hybrid RSA-OAEP-SHA256 + AES-256-GCM encryption, certificate rotation

### Health monitoring

- [Health Monitoring](references/guide/health/overview.md) — HealthStatus enum (Unknown/Healthy/Degraded/Unhealthy), per-rule required vs optional outcomes, expired feature flags, startup-throw vs runtime-rollback, accessing health via ConfigManager
- [ASP.NET Core Health Checks](references/guide/health/aspnetcore.md) — AddCocoarConfigurationHealthCheck() for ASP.NET Core health checks, custom name and tags, HealthStatus to HealthCheckResult mapping, /health endpoint integration
- [Logging & Diagnostics](references/guide/health/logging.md) — Microsoft.Extensions.Logging with source-generated LoggerMessage, Cocoar.Configuration log categories, event IDs by Debug/Information/Warning level, filtering by prefix
- [Performance Characteristics](references/guide/health/performance.md) — Partial re-evaluation, SHA-256 hash-based change detection, zero steady-state cost, one instance per config type, provider sharing by key, reference-equality reactive pipeline, 300ms debounce

### Testing

- [Test Overrides](references/guide/testing/overrides.md) — CocoarTestConfiguration with AsyncLocal isolation, ReplaceConfiguration vs AppendConfiguration, independent ReplaceSecretsSetup with AllowPlaintext for parallel-safe tests
- [Integration Testing](references/guide/testing/integration.md) — Bridging the xUnit AsyncLocal context gap, TestConfigurationContext fixture pattern, CocoarTestConfiguration.Apply/Clear in constructors, WebApplicationFactory integration tests
- [Testing Strategy](references/guide/testing/strategy.md) — Test-at-the-right-layer principles, test project structure, in-memory TestProviders with no I/O in Core.Tests, trait filters (Unit/Stress), deterministic active-waiting over fixed delays

### Analyzers

- [Analyzers & Source Generator](references/guide/analyzers/overview.md) — Built-in Roslyn analyzers (COCFG001-006) and the flags/entitlements source generator, diagnostics-at-a-glance table, suppression via pragma, attribute, and .editorconfig
- [Configuration Diagnostics](references/guide/analyzers/configuration.md) — COCFG diagnostics reference — COCFG001 secret path conflicts, COCFG002 rule dependency ordering, COCFG003 required-rule validation, COCFG005/006 duplicate and static-provider ordering
- [Flags Diagnostics & Source Generator](references/guide/analyzers/flags.md) — COCFLAG diagnostics — COCFLAG001 non-static ExpiresAt, COCFLAG002 abstract type in Register<T>, COCFLAG003 missing summary docs, plus the flags/entitlements source generator

### How-to

- [Migrating from IOptions](references/guide/how-to/from-ioptions.md) — Incremental IOptions/IConfiguration migration — FromIConfiguration bridge, IOptionsMonitor to IReactiveConfig, PostConfigure as last-write-wins rule, mapping table

### Migration

- [Migration v4 → v5](references/guide/migration/v4-to-v5.md) — v4 to v5 — ConfigManager.Create builder API, 10+ packages consolidated to 7, feature flags & entitlements, HttpPolling renamed to Http, Flag<T> to FeatureFlag<T>, health and resolver API renames
- [Migration v3 → v4](references/guide/migration/v3-to-v4.md) — v3 to v4 — no public API breaks; adds test overrides, Secret<T> X.509 encryption, secrets CLI, COCFG analyzers; internal provider contract moves from JsonElement to byte[] (custom providers only)
- [Migration v2 → v3](references/guide/migration/v2-to-v3.md) — v2 to v3 Type-First API migration — rule.File().For<T>() becomes rule.For<T>().FromFile(), config-aware .When(IConfigurationAccessor), provider-method rename table

### Reference

- [Package Overview](references/reference/packages.md) — NuGet package breakdown — Abstractions, Core, DI, AspNetCore, ConfigHub, Http, MicrosoftAdapter, WritableStore.Marten, Analyzers, Secrets CLI; dependency graph and which to install
- [Health API Reference](references/reference/health-api.md) — Health API reference — HealthStatus enum, ConfigManager.IsHealthy, IFlagsHealthSource, ASP.NET Core health check, OpenTelemetry meters and Activity source
- [CLI Commands Reference](references/reference/cli-commands.md) — cocoar-secrets CLI reference — encrypt, decrypt, generate-cert, convert-cert, cert-info; options, exit codes, RSA-OAEP-SHA256 + AES-256-GCM envelope
- [Analyzer Diagnostics Reference](references/reference/analyzer-diagnostics.md) — Roslyn diagnostics reference — COCFG001-006 (secret conflicts, rule ordering, required rules, duplicates) and COCFLAG001-003 flags; severities and suppression
- [Examples](references/reference/examples.md) — Runnable example projects in src/Examples — file layering, conditional rules, providers (command-line, HTTP, custom), tuple reactive, secrets, ASP.NET Core, testing overrides

The same content is online at https://docs.cocoar.dev/configuration/ (index for LLMs: https://docs.cocoar.dev/configuration/llms.txt).
