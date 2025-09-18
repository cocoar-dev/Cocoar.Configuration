# Changelog

## [Unreleased]

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

