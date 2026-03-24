# Changelog

## [5.1.0] — unreleased

### Added

- **Aggregate Rules** — group multiple sub-rules into a single logical unit with isolated error handling
  - `FromFiles(params string[])` — file layering shorthand: `rule.For<T>().FromFiles("base.json", $"base.{env}.json")`
  - `.Aggregate(r => [...])` — general-purpose rule grouping with full provider flexibility
  - Inner Required failures stay within the aggregate boundary (optional aggregate absorbs errors, required aggregate propagates)
  - `AggregateRuleManager` acts as a mini-engine: internal merge, change forwarding, health reporting
  - `SubManagers` property for ConfigHub drill-down into aggregate structure

- **`TypedProviderBuilder<T>`** — new base class for provider extension methods
  - `TypedRuleBuilder<T>` now inherits from `TypedProviderBuilder<T>`
  - Inside `.Aggregate()` lambdas, only provider methods are available (no recursive nesting)
  - Existing extension methods retargeted — no breaking changes

- **`IRuleManager` interface** — extracted from `RuleManager` for uniform engine handling
  - Both `RuleManager` and `AggregateRuleManager` implement `IRuleManager`
  - `RuleExecutionOutcome` enum promoted to standalone type

- **Documentation improvements**
  - New guide: Aggregate Rules (VitePress)
  - ADR-004: Aggregate Rules with Isolated Execution Boundary
  - Improved DI documentation: clarified resolution caching, Scoped vs Singleton behavior, injection patterns
  - New AggregateRules example project

## [5.0.0] — unreleased

### Added

**Feature Flags & Entitlements**
- Strongly-typed feature flags and entitlements built into the core `Cocoar.Configuration` package
- `IFeatureFlags<TConfig>` interface with `FeatureFlag<T>` properties — bare lambdas directly assignable
- `IEntitlements<TConfig>` interface with `Entitlement<T>` properties
- Fluent registration: `.UseFeatureFlags(f => f.Register<T>())` and `.UseEntitlements(e => e.Register<T>())`
- `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` for descriptor metadata
- Health integration — expired flag classes report `Degraded` automatically
- Roslyn source generator emits `CocoarFlagsDescriptors` at compile time
- `IFeatureFlagEvaluator` / `IEntitlementEvaluator` for REST evaluation endpoints
- Context resolvers at global, class, and property levels

**ConfigManager Builder API**
- `ConfigManager.Create()` static factory with fluent builder — replaces `new ConfigManager(...).Initialize()`
- `ConfigManager.CreateAsync()` for async initialization with `CancellationToken`
- Builder groups concerns: `.UseConfiguration()`, `.UseLogger()`, `.UseDebounce()`, `.UseSecretsSetup()`

**Package Consolidation (10+ → 7)**
- Secrets, X509Encryption, Flags merged into core `Cocoar.Configuration`
- Flags.Generator merged into `Cocoar.Configuration.Analyzers`
- Secrets.Abstractions merged into `Cocoar.Configuration.Abstractions`
- Same types, same namespaces — fewer packages to install

**Zero External Dependencies**
- Removed `System.Reactive` from all shipped packages
- Lightweight internal reactive primitives (~200 lines)
- Public API unchanged — `IObservable<T>` is BCL

**Other Improvements**
- Runtime recomputes are now fully async (no sync-over-async)
- Secrets memory safety: direct UTF-8 deserialization, DEK zeroing, no plaintext in error messages
- File provider security: symlink rejection, improved path traversal validation
- Health model simplification with `HealthTracker`

### Breaking Changes

- `ConfigManager` constructors and `Initialize()` → `internal`. Use `ConfigManager.Create()`
- `AddCocoarConfiguration()` → builder API: `c => c.UseConfiguration(rule => [...])`
- Secrets setup → `UseSecretsSetup()` extension instead of `setup.Secrets()`
- Testing: `ReplaceAllRules()` → `ReplaceConfiguration()`, `AppendTestRules()` → `AppendConfiguration()`

See [Migration Guide v4 → v5](/guide/migration/v4-to-v5) for details.

---

## [4.2.1] — 2026-02-03

### Fixed
- Interface reactive configs: `IReactiveConfig<IInterface>` now works for interfaces exposed via `ExposeAs<IInterface>()`

## [4.2.0] — 2026-02-03

### Added
- **Abstractions packages**: `Cocoar.Configuration.Abstractions` and `Cocoar.Configuration.Secrets.Abstractions`
- **`AllowPlaintext()`**: Conditionally allow plaintext values in `Secret<T>` for development/testing
- **Testing setup overrides**: `WithSetup()` and optional setup parameter on test override methods

### Fixed
- Deserialization failure logging (EventId 5100) instead of silent `null`
- DI registration ordering is now deterministic (sorted by type full name)
- Provider rebuild callback ordering fixed in `RuleProviderLease`

## [4.1.0] — 2026-01-11

### Fixed
- Provider consistency: optional rules now return empty objects with C# defaults consistently across all providers

## [4.0.0] — 2026-01-08

### Added
- **Testing Configuration Overrides**: `CocoarTestConfiguration` with `AsyncLocal<T>` isolation
- **Secrets Package**: `Secret<T>`, X.509 hybrid encryption (RSA-OAEP + AES-256-GCM), password-less certificates
- **Secrets CLI**: `generate-cert`, `convert-cert`, `cert-info`, `encrypt`, `decrypt`
- **Analyzers Package**: COCFG001–006

### Breaking
- Provider contract: `byte[]` instead of `JsonElement` (internal — consuming apps unaffected)

---

## [3.3.0] — 2025-10-23

### Added
- Rule naming: `.Named("name")` for health snapshots
- Enhanced health monitoring: `RuleHealthEntry` with Name, ProviderType, ConfigType, Skipped status
- `IConfigurationHealthService` auto-registered in DI

## [3.2.0] — 2025-10-23

### Added
- CommandLine provider with multiple switch prefix support

## [3.1.1] — 2025-10-19

### Fixed
- Enum string conversion: `JsonStringEnumConverter` for string-to-enum deserialization

## [3.1.0] — 2025-10-19

### Added
- Interface deserialization: `setup.Interface<I>().DeserializeTo<T>()`

## [3.0.0]

### Breaking
- **Type-First API**: `rule.File("...").For<T>()` → `rule.For<T>().FromFile("...")`
- **When() signature**: `Func<bool>` → `Func<IConfigurationAccessor, bool>`

### Added
- Config-aware conditional rules via `IConfigurationAccessor` in `.When()`

See [Migration Guide v2 → v3](/guide/migration/v2-to-v3) for details.

---

## [2.0.0] — 2025-09-30

### Breaking
- `Rule.From.File()` → `rule.File()` (lambda builder)
- `Bind.Type<T>().To<I>()` → `setup.ConcreteType<T>().ExposeAs<I>()`

## [1.1.0] — 2025-09-25
- `IReactiveConfig<T>` tuple support for atomic multi-config updates

## [1.0.0] — 2025-09-21
- First stable release: 204 tests, health monitoring, reactive configuration

## [0.9.0] — 2025-09-14
- Initial release
