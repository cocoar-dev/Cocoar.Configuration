---
description: Roslyn diagnostics reference — COCFG001-006 (secret conflicts, rule ordering, required rules, duplicates) and COCFLAG001-003 flags; severities and suppression
---

# Analyzer Diagnostics Reference

All diagnostics ship with the `Cocoar.Configuration` package. No separate install needed.

## Configuration Diagnostics (COCFG)

### COCFG001 — Secret Path Conflict

| | |
|---|---|
| **Severity** | Warning |
| **Category** | Cocoar.Configuration |
| **Code Fix** | No |

**Message:** Property '{0}' conflicts with secret property '{1}'. Consider using Secret&lt;T&gt; or renaming to avoid plaintext exposure.

A non-secret property has the same configuration path as a `Secret<T>` property. The non-secret rule could overwrite the encrypted value with plaintext.

See [guide](/guide/analyzers/configuration#cocfg001) for examples.

---

### COCFG002 — Rule Dependency Ordering

| | |
|---|---|
| **Severity** | Error |
| **Category** | Cocoar.Configuration |
| **Code Fix** | No |

**Message:** Rule for '{0}' depends on '{1}' which is not available yet. Move this rule after the '{1}' rule.

A rule uses `GetConfig<T>()` to read a type whose rule appears later in the list. Rules execute sequentially — dependencies must appear first.

See [guide](/guide/analyzers/configuration#cocfg002) for examples.

---

### COCFG003 — Required Rule Validation

| | |
|---|---|
| **Severity** | Warning |
| **Category** | Cocoar.Configuration |
| **Code Fix** | No |

**Message:** Required rule for '{0}' references '{1}' which may not exist. Application will fail to start if this resource is missing.

A `.Required()` rule references a file or resource that may not exist at runtime. If the resource is missing, the application will fail to start.

See [guide](/guide/analyzers/configuration#cocfg003) for examples.

---

### COCFG005 — Duplicate Unconditional Rules

| | |
|---|---|
| **Severity** | Info |
| **Category** | Cocoar.Configuration |
| **Code Fix** | No |

**Message:** Multiple unconditional rules for type '{0}'. Last rule will override earlier rules. Consider using .When() conditions or removing duplicates.

Multiple rules target the same type without conditions. Since rules merge with last-write-wins, earlier unconditional rules are fully overwritten — wasting provider I/O.

See [guide](/guide/analyzers/configuration#cocfg005) for examples.

---

### COCFG006 — Static Provider Ordering

| | |
|---|---|
| **Severity** | Info |
| **Category** | Cocoar.Configuration |
| **Code Fix** | No |

**Message:** Static/seed rule found after dynamic rules. Consider moving static rules first to ensure they're available to dynamic rules.

A static rule appears after dynamic rules. Since rules merge property by property (later wins), a static rule at the end always overrides dynamic sources.

See [guide](/guide/analyzers/configuration#cocfg006) for examples.

---

## Feature Flags Diagnostics (COCFLAG)

### COCFLAG001 — Non-Static ExpiresAt

| | |
|---|---|
| **Severity** | Warning |
| **Category** | CocoarFlags |
| **Code Fix** | No |

**Message:** '{0}.ExpiresAt' could not be statically determined. The class will be registered with ExpiresAt = DateTimeOffset.MinValue (treated as expired). Use a DateTimeOffset literal: `new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)`.

The source generator couldn't evaluate `ExpiresAt` at compile time. The class defaults to `DateTimeOffset.MinValue` — treated as already expired, causing health to report `Degraded`.

See [guide](/guide/analyzers/flags#cocflag001) for examples.

---

### COCFLAG002 — Abstract Type Registered

| | |
|---|---|
| **Severity** | Warning |
| **Category** | CocoarFlags |
| **Code Fix** | No |

**Message:** '{0}' is abstract and cannot be used with Register&lt;T&gt;(). Use a concrete subclass instead.

`Register<T>()` was called with an abstract class. Abstract classes can't be instantiated as flag or entitlement instances.

See [guide](/guide/analyzers/flags#cocflag002) for examples.

---

### COCFLAG003 — Missing Description

| | |
|---|---|
| **Severity** | Info |
| **Category** | CocoarFlags |
| **Code Fix** | No |

**Message:** Property '{0}' on '{1}' has no &lt;summary&gt; XML doc comment. Add a description so it appears in flag/entitlement descriptors.

A `FeatureFlag<T>` or `Entitlement<T>` property has no `<summary>` XML doc comment. Descriptions are surfaced through `IFeatureFlagsDescriptors` / `IEntitlementsDescriptors` and the REST API.

See [guide](/guide/analyzers/flags#cocflag003) for examples.

---

## Summary Table

| ID | Severity | Category | Code Fix | What It Catches |
|---|---|---|---|---|
| COCFG001 | Warning | Configuration | No | Secret path conflicts |
| COCFG002 | Error | Configuration | No | Rule dependency ordering |
| COCFG003 | Warning | Configuration | No | Required rule missing resource |
| COCFG005 | Info | Configuration | No | Duplicate unconditional rules |
| COCFG006 | Info | Configuration | No | Static provider ordering |
| COCFLAG001 | Warning | CocoarFlags | No | Non-static ExpiresAt |
| COCFLAG002 | Warning | CocoarFlags | No | Abstract type registered |
| COCFLAG003 | Info | CocoarFlags | No | Missing property description |

## Suppressing Diagnostics

```csharp
// In code
#pragma warning disable COCFG005
rules.For<AppSettings>().FromFile("a.json"),
rules.For<AppSettings>().FromFile("b.json")
#pragma warning restore COCFG005

// Via attribute
[SuppressMessage("Cocoar.Configuration", "COCFG005")]

// Via .editorconfig
[*.cs]
dotnet_diagnostic.COCFG005.severity = none
```
