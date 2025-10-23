# Changelog

## [3.2.0] - 2025-10-23

### Added
- **CommandLine Provider**: New built-in provider for parsing command-line arguments
  - Supports multiple switch prefixes simultaneously: `["--", "-", "/"]` or custom semantic prefixes like `["@", "#", "%"]`
  - Example: `.FromCommandLine(["--", "-", "/"])` accepts all three styles in the same command line
  - Automatic longest-match-first algorithm prevents ambiguity (e.g., `--host` matches `"--"` before `"-"`)
  - Flexible API: `.FromCommandLine()`, `.FromCommandLine("prefix_")`, `.FromCommandLine(["-"])`
  - Supports nested configuration with `:` or `__` separators (e.g., `--database:host=localhost` or `--database__host=localhost`)
  - Prefix filtering to map different arguments to different config types
  - Three argument formats: `--key=value`, `--key value`, `--flag` (boolean)
  - Enables self-documenting CLIs: `invoke.exe @host=server #issue=123 %env=prod`

- **Test Example**: New CommandLineExample project with 6 comprehensive integration tests

### Improved
- **Provider Architecture**: Enhanced ProviderOptions vs QueryOptions separation across CommandLine, Environment, and FileSource providers
  - `ProviderOptions` now only contains provider-level configuration (shared across queries)
  - `QueryOptions` contains query-specific parameters (different per rule)
  - Enables proper provider sharing and rule-specific configuration

### Removed
- **Dead Code Cleanup**: Removed 4 unused FileSystemObservable files (~140 lines)
  - Existing `FileWatcherObservable` is used and battle-tested


## [3.1.1] - 2025-10-19

### Fixed
- **Enum String Conversion**: Added `JsonStringEnumConverter` to support deserializing enums from string values in JSON/environment variables
  - Fixes issue where enum properties (e.g., `LogEventLevel.Debug`) would fail when provided as strings (e.g., `"Debug"`)
  - Common scenario: Visual Studio setting `Logging={"LogLevel":{"Microsoft.AspNetCore.Watch":"Debug"}}` as environment variable
  - Now supports case-insensitive string-to-enum conversion for all enum types

## [3.1.0] - 2025-10-19

### Added
- **Interface Deserialization Support**: New `setup.Interface<I>().DeserializeTo<T>()` API for deserializing interface-typed properties in configuration classes
  - Solves the problem where configuration classes with interface properties (e.g., `public ILoggingConfig Logging { get; set; }`) would fail deserialization from JSON sources
  - Common scenario: Setting logging configuration via environment variables (e.g., `Logging__LogLevel__Default=Debug`) or when Visual Studio injects logging configuration for hot reload
  - Supports deeply nested interface properties at any depth
  - Example: `setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>()`
  - Includes comprehensive test coverage for nested and deeply nested scenarios

### Usage Example
```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromEnvironment()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
    
    // Map interface properties to concrete types
    setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>()
]);
```

## [3.0.0]

### Added
- **Config-Aware Conditional Rules**: `.When()` method now receives `IConfigurationAccessor` parameter, allowing rules to be conditionally executed based on configuration from earlier rules
  - Example: `rule.For<PremiumFeatures>().FromFile("premium.json").When(accessor => accessor.GetRequiredConfig<TenantSettings>().Tier == "Premium")`
  - Enables powerful dynamic configuration scenarios (multi-tenant, environment-based, feature flags)
- **ConditionalRulesExample**: New example project demonstrating config-aware conditional rules

### Changed
- **Type-First API**: Refactored rule builder API from Provider-First to Type-First pattern for better discoverability and type safety
  - Old: `rule.File("...").For<T>()` → New: `rule.For<T>().FromFile("...")`
  - All provider methods renamed: `File()` → `FromFile()`, `Environment()` → `FromEnvironment()`, etc.
  - Consistent pattern across all providers (File, Environment, Static, Observable, HttpPolling, MicrosoftSource)

### Breaking
- **Type-First API Changes** (⚠️ MAJOR):
  - `rule.File(...)` → `rule.For<T>().FromFile(...)`
  - `rule.Environment(...)` → `rule.For<T>().FromEnvironment(...)`
  - `rule.StaticJson(...)` → `rule.For<T>().FromStaticJson(...)`
  - `rule.Static<V>(...)` → `rule.For<T>().FromStatic(...)`
  - `rule.Observable(...)` → `rule.For<T>().FromObservable(...)`
  - `rule.HttpPolling(...)` → `rule.For<T>().FromHttpPolling(...)`
  - `rule.MicrosoftSource(...)` → `rule.For<T>().FromMicrosoftSource(...)`
  - `rule.FromProvider<P,O,Q>(...)` → `rule.For<T>().FromProvider<T,P,O,Q>(...)`
- **When() Signature Change**: `.When(Func<bool>)` → `.When(Func<IConfigurationAccessor, bool>)`
  - Old: `rule.File("...").When(() => condition).For<T>()`
  - New: `rule.For<T>().FromFile("...").When(_ => condition)` or with accessor: `.When(accessor => accessor.GetRequiredConfig<Other>().Property)`

### Removed
- Removed test helper methods from production provider APIs (`CreateRule` methods in FileSourceProvider, StaticJsonProvider, ObservableProvider)
  - These were internal test utilities mistakenly exposed in public API surface

### Migration from v2.0
```csharp
// v2.0 (Provider-First)
builder.AddCocoarConfiguration(rule => [
    rule.File("config.json").Select("App").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>(),
    
    // Conditional rule (old signature)
    rule.File("premium.json")
        .When(() => isPremium)
        .For<PremiumFeatures>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);

// v3.0 (Type-First + Config-Aware When)
builder.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("config.json").Select("App"),
    rule.For<AppSettings>().FromEnvironment("APP_"),
    
    // Conditional rule (new signature with accessor)
    rule.For<PremiumFeatures>().FromFile("premium.json")
        .When(accessor => accessor.GetRequiredConfig<TenantSettings>().Tier == "Premium")
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

See [Migration Guide v2→v3](docs/migration-v2-to-v3.md) for detailed migration instructions.

## [2.0.0] - 2025-09-30

### Changed
- **Builder API Modernization**: Replaced static `Rule.From.*` API with function-based `RulesBuilder` pattern (`rule => rule.File(...)`) for more intuitive configuration
- **Setup API Modernization**: Replaced `Bind.Type<T>().To<I>()` API with function-based `SetupBuilder` pattern (`setup => setup.ConcreteType<T>().ExposeAs<I>()`) with clearer naming
- Renamed "binding" terminology to "exposure" throughout the API and documentation for clarity

### Breaking
- `Rule.From.File()`, `Rule.From.Environment()`, etc. replaced with `RulesBuilder` lambda parameter: `builder.AddCocoarConfiguration(rule => [rule.File(...), rule.Environment(...)])`
- `Bind.Type<T>().To<I>()` replaced with `SetupBuilder` lambda parameter: `setup => setup.ConcreteType<T>().ExposeAs<I>()`
- `ServiceRegistrationOptions` and `Register.Add<T>()` replaced with direct lifetime configuration on `ConcreteTypeSetup`

### Migration from v1.x
```csharp
// v1.x
builder.AddCocoarConfiguration([
    Rule.From.File("config.json").Select("App").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
], [
    Bind.Type<AppSettings>().To<IAppSettings>()
]);

// v2.0
builder.AddCocoarConfiguration(rule => [
    rule.File("config.json").Select("App").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

See [Migration Guide v1→v2](docs/migration-v1-to-v2.md) for detailed migration instructions.

## [1.1.0] - 2025-09-25

`IReactiveConfig<T>` now supports tuples as the generic type, e.g.
`IReactiveConfig<(AppSettings, DbSettings)>`.
When used with a tuple, all element types are recomputed and emitted atomically in the same pass.
This guarantees you never see a mix of old and new values across different configs.

## [1.0.0] - 2025-09-21

### 🎉 First Stable Release

This marks the first stable release of Cocoar.Configuration with production-ready features and comprehensive testing infrastructure.

### Added
- **Comprehensive Test Suite**: **204 automated tests** covering core functionality, providers, edge cases, and stress scenarios
- **Health Monitoring System**: Complete health monitoring with `IConfigurationHealthService`, health snapshots, and experimental metrics export hooks
- **Reactive Configuration**: Auto-registered `IReactiveConfig<T>` for every configuration type in dependency injection
- **Enhanced Documentation**: 
  - Streamlined README with practical examples and accurate feature descriptions
  - Dedicated health monitoring guide (`docs/health-monitoring.md`)
  - Reactive configuration guide (`docs/reactive-config.md`)

### Testing Improvements
- **Integration Tests**: Multi-provider composition, rule ordering, recompute pipeline validation
- **Stress & Performance Tests**: High-frequency changes, large JSON handling, concurrent access scenarios
- **Provider Battle Tests**: HTTP headers/caching, Microsoft adapter integration, environment variable edge cases
- **Health Pipeline Tests**: Status derivation, recovery scenarios, observable health updates
- **Fuzz Testing**: Random change sequences maintaining correctness and deterministic results
- **File Provider Resilience**: FileSystemWatcher ↔ polling fallback and automatic recovery testing

### Enhanced
- **File Provider**: Improved resilience with automatic polling fallback when FileSystemWatcher fails
- **Test Organization**: Restructured test projects with clear separation (Core.Tests, Providers.Tests)
- **Example Projects**: Updated all 12 example projects with improved clarity and modern patterns
- **Documentation Structure**: Consolidated scattered documentation into focused, practical guides

### Quality & Reliability
- **204 comprehensive tests** ensuring stability across all components and failure scenarios  
- **Production-tested patterns** validated through extensive integration and stress testing
- **Error-resilient implementations** with proper failure handling and recovery mechanisms
- **Continuous integration** ensuring every change maintains stability and correctness

## [0.15.0] - 2025-09-18

### Fixed
- **FileSystemWatcher File Locking:** Fixed file locking conflicts in FileSourceProvider by implementing proper file sharing (FileShare.ReadWrite) to prevent missed configuration changes and IOException conflicts in production scenarios.
- **Testing Anti-Patterns:** Eliminated flaky test behavior by removing emission counting anti-patterns and implementing proper final state validation in reactive configuration tests.

### Changed
- Enhanced test organization with better naming conventions and regional structuring for improved maintainability.
- Implemented active waiting patterns and controllable testing infrastructure (ObservableProvider) for reliable FileSystemWatcher testing.
- Added comprehensive stress testing suite with multi-iteration reliability validation.

## [0.14.0] - 2025-09-17

### Added
- StaticJsonProvider now supports JSON strings directly via `Rule.From.StaticJson(jsonString)`.

### Changed
- StaticJsonProvider instances are no longer shared between rules (null-key pattern) to prevent configuration data leakage between different rules.

## [0.13.0] - 2025-09-17

### Changed
- License changed from MIT to Apache-2.0 to provide an explicit patent grant and consistent downstream attribution via `NOTICE`. Previous released versions remain under MIT.

## [0.12.0] - 2025-09-17

### Added
- Reactive configuration channel: automatic `IReactiveConfig<T>` for every config type (singleton, hash-gated, error-resilient).
- Auto DI registration for `IReactiveConfig<T>` (opt-out via `DisableAutoReactiveRegistration`).

### Performance
- Streaming JSON → MD5 hashing pipeline for selection & emission gating (reduced allocations, faster change detection).
- Partial recompute optimizations (earliest changed rule restart) documented and hardened.

### Documentation
- README overhaul: simplified quick start, lifetimes, reactive defaults, full examples table with direct links.
- Added `DEEP_DIVE.md` (advanced scenarios, tuning, dynamic dependencies).
- Updated architecture doc: streaming MD5 hashing + reactive channel section.
- Clarified partial recompute & reactive channel in Concepts; added migration note for merged reactive implementation.
- Converted inline doc code references to clickable links across docs & examples.

## [0.11.1] - 2025-09-16

- Refactor configuration options to set default 'Required' value to false and remove unnecessary 'Optional' calls in tests
- Add DI Options for Cocoar.Configuration.AspNetCore - AddCocoarConfiguration

## [0.11.0] - 2025-09-16

### Added
- New Binding System (`Bind.Type<T>().To<IInterface>()`) enabling interface mapping independent of DI.
- `BindingRegistry` and runtime validation for interface→concrete compatibility.
- `Cocoar.Configuration.DI` package: separation of concerns between interface binding and DI registration.
- `ServiceRegistrationOptions.DefaultRegistrationLifetime(null)` to fully disable auto-registration.
- Keyed service registration refinement via explicit `options.Register.Add<T>(lifetime, key)` model.
- Comprehensive documentation: [docs/BINDING.md](docs/BINDING.md), updated README Binding vs DI Registration section.

### Changed
- Auto-registration default clarified (Scoped) and now explicitly optional.
- Examples reorganized: added dedicated Binding, DI, ServiceLifetimes demos; README minimized to one-liners.

### Internal / Quality
- Expanded test coverage for service lifetimes, keyed registrations, binding validation.
- Documentation cleanup removing unreleased prototype terminology.

### Migration Notes
- For consumers of 0.9.x: follow 0.10.0 migration first (selection API changes), then optionally adopt bindings (purely additive).


## [0.10.0] - 2025-09-15

### Breaking
- Removed query-level `configurationPath` and `targetPath`; use rule-level `.Select(...)` and `.MountAt(...)`.

### Highlights
- Rule Selection Simplification: centralized rule-level selection (`.Select`) clarifies intent and simplifies providers.
- Incremental Recompute Engine: recompute only from earliest changed rule; unchanged prefix reused from cached flattened contributions.
- Change Gating & Noise Reduction: selection-hash gating skips recompute when the selected subtree is unchanged.
- Fast Cancellation & Coalescing: mid-pass cancellation plus immediate + trailing debounce collapses bursts without added latency for first change.
- Coalescer Extraction: new `RecomputeCoalescer` class isolates change accumulation & timing logic.
- Deletion Propagation: removed keys from updated rule contributions are pruned from final configs (no stale "zombie" keys).
- Snapshot Model Simplification: dropped merged/unflattened snapshot storage; now only per-rule flattened contribution + selection hash.
- Static Rule Set Decision: rules immutable post-initialization (use `UseWhen` to conditionally participate).
- Provider & Query Cleanup: normalized option/query shapes; file examples updated to selection chaining.
- Test Suite Expansion: new tests for partial recompute, cancellation, selection hash gating, key deletions; added explanatory headers.
- Performance Foundations: hashing + earliest-index logic lays groundwork for future benchmarks.
- Documentation & Examples Refresh: updated architecture & provider docs to Fetch → Select → Mount → Merge; removed legacy two-argument file section usage.
- API Surface Cleanup: removed obsolete params & helpers; pruned unused hash utilities.
- Reliability & Determinism: provider injection seam enables deterministic tests; strengthened dynamic dependency scenarios.
- Logging & Error Handling Consistency: resilient change-trigger error handling while preserving required/optional semantics.

### Migration Summary
```diff
- Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("appsettings.json", configurationPath: "A:B")).For<MyCfg>().Build();
+ Rule.From.File("appsettings.json").Select("A:B").For<MyCfg>().Build();

- Rule.From.HttpPolling(_ => HttpPollingRuleOptions.FromPath("service.json", configurationPath: "Service")).For<Service>().Build();
+ Rule.From.HttpPolling("service.json").Select("Service").For<Service>().Build();

- Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("base.json", targetPath: "Config:Base"))...
+ Rule.From.File("base.json").MountAt("Config:Base")...
```


## [0.9.2] - 2025-09-15

- Added: Concise overloads Rule.From.File(...), Rule.From.Environment(...).
- Added: .MountAt fluent API for rule mounting.
- Migration: Replace targetPath: "A:B" with .MountAt("A:B").

## [0.9.1] - 2025-09-14
Branding / assets update.

- Replaced NuGet package icon (`package-icon.png`).
- Updated README image.
- Updated GitHub social preview images (`social-preview.png`, `social-preview-small.png`) stored at repo root.
- No functional/code changes.

## [0.9.0] - 2025-09-14
Initial release 🎉

- Deterministic ordered configuration layering (last-write-wins)
- Strongly typed DI (no IOptions<T>)
- Providers: File, Environment, Static, HTTP Polling, Microsoft Adapter
- Dynamic rule factories & atomic snapshot recompute
- DI lifetimes & keyed registrations
- Examples included under `src/Examples/`


