---
description: AggregateRuleManager wraps N sub-rules, byte-merges their results, and contains inner Required failures within the aggregate boundary; FromFiles sugar
---

# ADR-004: Aggregate Rules with Isolated Execution Boundary

**Status:** Accepted
**Date:** 2026-03-24
**Decision Makers:** Core Team
**Type:** Feature / Architecture
**Related:** File layering verbosity, ConfigHub observability requirements

---

## Context

Real-world Cocoar.Configuration setups are verbose when layering base files with environment-specific overlays. Each configuration type typically needs 2-3 separate rules (base file, environment override, environment variables). With 7+ config types, this leads to 20+ rules where the relationship between base and overlay is implicit.

Additionally, independent rules have no shared error boundary. If a Required rule fails, it rolls back the entire recompute — there is no way to express "this group of rules should fail together or succeed together."

### Approaches Considered

**1. Template strings (e.g., `FromFile("config(.{env}).json")`):**
Rejected — requires a mini DSL/parser inside strings, not discoverable, no IntelliSense.

**2. `.WithOverlay()` fluent chain:**
Rejected — repeats the full file path for each overlay, only slightly less verbose.

**3. Flat expansion (initial implementation):**
Rejected after analysis — expands `AggregateConfigRule` into individual `ConfigRule` instances with group metadata. Inner Required semantics were incorrect: a Required sub-rule would throw and kill the entire recompute even when the aggregate was optional. This would require a behavior change later, creating a silent breaking change.

**4. AggregateRuleManager (chosen):**
A dedicated manager that wraps N internal `RuleManager` instances, executes them internally, merges their results, and presents one output to the engine. Inner failures are contained within the aggregate boundary.

## Decision

Introduce `AggregateRuleManager` implementing `IRuleManager` — the same interface as `RuleManager`. The engine treats both uniformly. Internally, `AggregateRuleManager`:

1. Creates one `RuleManager` per sub-rule, sharing the same `ProviderRegistry` (no duplicate FileWatchers).
2. On `ComputeAsync`: executes each internal manager sequentially, parses results to `MutableJsonObject`, deep-merges them, and serializes back to bytes via `MutableJsonDocument.ToUtf8Bytes`.
3. Catches inner Required failures within the aggregate boundary. If the aggregate is optional, failures are absorbed (Degraded). If required, they propagate (Rollback).
4. Subscribes to all internal managers' `Changes` and forwards signals to the outer engine. Internal managers hit cache for unchanged sources — only the changed source re-fetches.
5. Exposes `SubManagers` for ConfigHub drill-down into the aggregate structure.

### Public API

```csharp
// FromFiles — syntactic sugar
rule.For<DbSettings>()
    .FromFiles("db.json", $"db.{env}.json")
    .Required()

// Aggregate — full control
rule.For<DbSettings>()
    .Aggregate(r => [
        r.FromFile("db.json").Required(),
        r.FromFile($"db.{env}.json")
    ])
```

The `Aggregate` lambda receives `TypedProviderBuilder<T>` (not `TypedRuleBuilder<T>`), preventing recursive nesting.

### Builder Hierarchy

```
TypedProviderBuilder<T>       ← base, provider extensions target this
  └── TypedRuleBuilder<T>     ← adds Aggregate(), FromFiles()
```

Existing extension methods retargeted from `TypedRuleBuilder<T>` to `TypedProviderBuilder<T>`. No breaking changes — `TypedRuleBuilder<T>` inherits all methods.

## Consequences

✅ Correct Required semantics from day one — no silent behavior changes later
✅ Full observability via `SubManagers` for ConfigHub tree display
✅ One change signal per aggregate (not N signals for N sub-rules)
✅ Provider sharing via existing `ProviderRegistry` — no resource duplication
✅ Byte-level merge path — no string allocations (`MutableJsonMerge` + `Utf8JsonWriter`)
✅ Minor version — purely additive, existing rules unchanged

⚠️ Additional complexity: `IRuleManager` interface extraction, `AggregateRuleManager` implementation
⚠️ Internal merge adds a parse → merge → serialize step (negligible for 2-3 sub-rules)

## References

- `src/Cocoar.Configuration/Rules/IRuleManager.cs` — Common interface
- `src/Cocoar.Configuration/Rules/AggregateRuleManager.cs` — Implementation
- `src/Cocoar.Configuration/Rules/AggregateConfigRule.cs` — Rule definition
- `src/Cocoar.Configuration/Fluent/AggregateRulesExtensions.cs` — `Aggregate()` + `FromFiles()` API
- `src/Cocoar.Configuration/Fluent/TypedProviderBuilder.cs` — Base builder preventing recursive nesting
