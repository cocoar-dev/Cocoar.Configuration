# Changelog

## [Unreleased]
_No changes yet._

## [0.9.3] - 2025-09-15

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
- API Surface Cleanup: removed obsolete params & helpers; `.Pick` renamed; pruned unused hash utilities.
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

Rename any `.Pick(...)` usage to `.Select(...)`.

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
