# Cocoar.Configuration – Agreed Behavior and Follow-ups

This document captures the agreed functional model and the planned improvements we want to implement.

## Current behavior (implemented)
- Rules are evaluated in configured order; last-write-wins merge of JSON objects using colon-keys.
- Dynamic dependencies: during a recompute, later rules can read the in-progress snapshot (any type) produced by earlier rules.
- Any provider change triggers a full recompute across all rules and types; cache swaps atomically.
- Rule severity: required rules fail the recompute on error; optional rules are skipped on failure.
- Providers: File and HTTP can emit changes; Environment is snapshot-only by default.
- DI: resulting config types are registered as singletons and injected as snapshots; use ConfigManager for always-fresh reads.
- Arrays: replace fully (no merge) – by design.
- StaticJsonProvider: explicit seeding when needed (no implicit defaults). Factories run during recompute, optional targetPath, and no change emissions. Prefer real sources; use sparingly to guarantee existence for dependent rules.

## Target behavior updates (status)
1) DI lifetimes evolution
   - Today: singletons (snapshot at resolution time).
   - Future: add a refreshable injection pattern (e.g., IConfigMonitor<T> / IConfigurationAccessor<T>) to observe recomputes.
   - Consider scoping for ASP.NET Core scenarios where desired.

2) Cycle guardrails and diagnostics
   - Detect or warn on likely cycles across types/rules.
   - Provide tracing to surface rule ordering and dependency reads during recompute.

3) Documentation and samples
   - ✅ Expand README examples for dynamic dependencies (file → http, env → http, etc.).
   - ✅ Call out ordering guidelines and cycle avoidance explicitly.
   - ✅ Added comprehensive test coverage for all README examples.

4) IDE-time analysis and validation
   - ✅ Basic runtime analysis implemented (logs warnings during ConfigManager.Initialize())
   - Future: Source generators or Roslyn analyzers for compile-time validation
   - Goal: Show IDE warnings for missing dependencies, rule ordering issues, and GetRequiredConfig<T> calls without corresponding rules
   - Consider integration with MSBuild for build-time validation

## Open questions
- Static provider and required semantics: ✅ Basic analysis implemented - warns about missing dependencies and rule ordering
- Environment change notifications: do we want an opt-in watcher/polling mode?

## Action items
- [ ] Design refreshable DI abstractions and minimal adapter for ASP.NET Core.
- [ ] Add simple cycle detection/warnings and recompute trace logs.
- [ ] Update README (DI section and limitations) once lifetimes evolve.
- [ ] Explore source generators for compile-time configuration validation and IDE diagnostics.
- [x] Comprehensive README examples with test coverage (completed 2025-09-11).
- [x] Naming consistency improvements across codebase (completed 2025-09-11).
- [x] Basic static provider analysis and rule validation (completed 2025-09-11).

---
Owner: cocoar-dev
Last updated: 2025-09-11
Status: Updated after comprehensive naming improvements and README validation implementation
