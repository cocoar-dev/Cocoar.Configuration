---
description: Group sub-rules into one unit with FromFiles file-layering shorthand and Aggregate() over mixed providers, aggregate-level vs sub-rule Required semantics
---

# Aggregate Rules <Badge type="info" text="ADV" />

Aggregate rules group multiple sub-rules into a single logical unit. The group merges its sub-rules internally and presents one result to the configuration engine.

## The Problem

File-layering setups are verbose — each config type needs separate rules for base, environment overlay, and local overrides:

```csharp
rule => [
    rule.For<DbSettings>().FromFile("db.json"),
    rule.For<DbSettings>().FromFile($"db.{env}.json"),
    rule.For<DbSettings>().FromFile("db.local.json"),
    rule.For<DbSettings>().FromEnvironment("DB_"),
]
```

More importantly, these rules are independent — if one fails, it fails in isolation with no concept of "this group of files belongs together."

## FromFiles — File Layering Shorthand

`FromFiles` bundles multiple file paths into one aggregate rule:

```csharp
rule => [
    rule.For<DbSettings>()
        .FromFiles("db.json", $"db.{env}.json", "db.local.json")
        .Required(),
    rule.For<DbSettings>().FromEnvironment("DB_"),
]
```

Files are merged in order (left to right). Files that don't exist are silently skipped. `.Required()` means the aggregate must produce data — at least one file must exist.

## Aggregate — Full Control

For non-file sources or mixed providers, use `.Aggregate()`:

```csharp
rule.For<DbSettings>()
    .Aggregate(r => [
        r.FromFile("db.json").Required(),
        r.FromFile($"db.{env}.json"),
        r.FromEnvironment("DB_")
    ])
    .Required()
```

The lambda receives a `TypedProviderBuilder<T>` — all provider methods (`FromFile`, `FromEnvironment`, `FromHttp`, etc.) are available, but `Aggregate` and `FromFiles` are not. This prevents recursive nesting.

## Required Semantics

Required works at two independent levels:

### Aggregate-Level Required

`.Required()` on the aggregate means "the merged result must not be empty":

```csharp
rule.For<DbSettings>()
    .FromFiles("db.json", $"db.{env}.json")
    .Required()  // At least one file must contribute data
```

### Sub-Rule Required

`.Required()` on an inner sub-rule means "this source is critical for the group":

```csharp
rule.For<DbSettings>()
    .Aggregate(r => [
        r.FromFile("db.json").Required(),   // Must exist
        r.FromFile($"db.{env}.json")        // Optional overlay
    ])
```

### Interaction

| Aggregate | Sub-Rule | Sub-Rule Fails | Behavior |
|-----------|----------|----------------|----------|
| optional | optional | — | Degraded, continues |
| optional | required | — | Failure absorbed, Degraded |
| required | optional | — | OK if others contribute |
| required | required | — | Rollback (exception) |

Inner Required failures **never escape** an optional aggregate. This is the key difference from independent rules — the aggregate acts as a boundary.

## Health & Observability

The aggregate reports its own health status. Internally failed sub-rules are not visible to the health tracker — the aggregate is the unit:

- Aggregate produces data → **Healthy**
- Aggregate fails (optional) → **Degraded**
- Aggregate fails (required) → **Unhealthy**

For detailed drill-down (e.g., in ConfigHub), the aggregate exposes its sub-rule managers via `SubManagers`.

## Conditional & Config-Aware Sub-Rules

`.When()` and config-aware provider options work inside aggregates:

```csharp
rule.For<DbSettings>().Aggregate(r => [
    r.FromFile("db.json"),
    r.FromFile("db.prod.json")
        .When(accessor => accessor.GetConfig<EnvConfig>()?.IsProduction == true),
    r.FromFile(accessor => {
        var region = accessor.GetConfig<RegionConfig>();
        return FileSourceRuleOptions.FromFilePath($"db.{region?.Name}.json");
    })
])
```

::: warning Accessor Timing
Sub-rules inside an aggregate see the configuration state from **before** the aggregate — not from sibling sub-rules. The aggregate is an atomic unit: it merges internally first, then contributes its result to the engine as a whole. Config-aware dependencies on other types that were resolved by rules **before** the aggregate work correctly.
:::

## When to Use What

| Scenario | Use |
|----------|-----|
| Base + environment overlay files | `FromFiles("base.json", $"base.{env}.json")` |
| Mixed providers in one group | `.Aggregate(r => [...])` |
| Independent sources, no grouping needed | Separate rules (existing behavior) |
