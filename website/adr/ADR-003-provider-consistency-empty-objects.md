# ADR-003: Fix Provider Inconsistency - Optional Rules Always Return Objects

**Status:** Accepted
**Date:** 2025-01-11
**Decision Makers:** Core Team
**Type:** Bug Fix
**Related:** PART2 Article (Optional vs Required Rules)

---

## Context

Cocoar.Configuration had a **bug where providers handled missing or unavailable data inconsistently**, leading to unpredictable behavior and requiring workarounds.

### The Problem: Inconsistent Provider Behavior

Currently, providers handle "no data" scenarios differently based on their source type:

**Collection-Based Providers** (Always succeed with empty):
```csharp
// EnvironmentVariableProvider
rule.For<Config>().FromEnvironment("APP_")
// No matching env vars → Returns {} → Config with C# defaults

// CommandLineArgumentProvider
rule.For<Config>().FromCommandLine("--app:")
// No matching args → Returns {} → Config with C# defaults
```

**Source-Based Providers** (Throw when source unavailable):
```csharp
// FileSourceProvider
rule.For<Config>().FromFile("config.json")
// File doesn't exist → Throws FileNotFoundException → null (unavailable)

// HttpProvider
rule.For<Config>().FromHttp("http://api/config")
// Endpoint down → Throws → null (unavailable)
```

### Real-World Impact

**User reports inability to access configuration:**

```csharp
builder.AddCocoarConfiguration(rule => [
    rule.For<PersistenceConfig>().FromFile("config.json")
]);

var config = builder.GetCocoarConfigManager().GetConfig<PersistenceConfig>();
// config is NULL when file doesn't exist
```

**Current workarounds:**

```csharp
// Workaround 1: Add fake environment rule
rule.For<Config>().FromFile("config.json"),
rule.For<Config>().FromEnvironment("NONEXISTENT_")  // Hack: always returns {}
```

```csharp
// Workaround 2: Explicit FromStatic for defaults
rule.For<Config>().FromStatic(_ => new Config()),  // Explicit defaults
rule.For<Config>().FromFile("config.json")
```

The first workaround is **implicit and unclear**. The second is better but required for every optional configuration.

### Semantic Confusion

The system conflates two orthogonal concepts:

1. **Source Availability**: Does the data source exist/respond?
2. **Data Availability**: Does the source contain data for this type?

Example scenarios that are semantically different but treated the same:

```csharp
rule.For<Config>().FromFile("config.json").Select("App")
```

- File doesn't exist → Throws → null
- File exists, "App" section missing → Throws KeyNotFoundException → null
- File exists, "App" is `{}` → Returns empty object

**All three should behave differently!**

### Current "Last Known Good" Behavior

The system has **asymmetric failure handling**:

**Required Rules** (`Required: true`):
- Provider throws → Exception propagates to `RecomputeAllConfigurationsSafe`
- Entire recompute is **rolled back** via `_state.RollbackUpdate()`
- **All types preserve their previous values** from `_configs` dictionary
- App continues with last known good configuration for all types

**Optional Rules** (`Required: false`, default):
- Provider throws → `HandleFailure()` skips the rule's contribution
- `LastJsonContribution` is left `null` for that rule
- Recompute **continues** with other rules
- If this was the **only rule** for a type → Type not in `mergedConfigs` → **GetConfig returns null**

From PART2 article (`.local/dev.to/PART2...`):

> **When an optional rule fails:**
> - Rule is skipped for that recompute
> - App uses **last known good** value for that type **(if none exists, that config type is unavailable)**

This behavior is **intentional per documentation**, but creates the problem: **"last known good" only exists if the rule succeeded at least once.**

**First-time failures have no history** → null instead of defaults.

---

## Decision

**All providers return empty JSON objects (`{}`) when they have no data, regardless of reason. Health monitoring tracks source availability separately.**

This fixes the inconsistency bug and aligns all providers with the correct "graceful degradation" behavior that was already working for collection-based providers.

### Core Principle

**Data Flow** (always flows):
- Provider → Always returns valid JSON (possibly `{}`)
- RuleManager → Always contributes data (include: true)
- Type → Always available with at least C# defaults

**Health Flow** (tracks issues):
- Provider throws → Exception caught by RuleManager
- RuleManager → Tracks exception in `LastFailureException`
- Health Status → Degraded with error details
- Multiple issues can be tracked per rule

### Behavior Changes

| Scenario | Old Behavior | New Behavior |
|----------|-------------|--------------|
| File doesn't exist | Throws → Skip → null | Returns `{}` → Object with defaults, Health = Degraded |
| HTTP endpoint down | Throws → Skip → null | Returns `{}` → Object with defaults, Health = Degraded |
| No matching env vars | Returns `{}` → Object | Returns `{}` → Object, Health = Healthy |
| Select path missing | Throws → Skip → null | Returns `{}` → Object with defaults, Health = Degraded |

### Benefits

**1. Consistency**
```csharp
// All providers work the same way
rule.For<Config>().FromFile("config.json")           // Always returns object
rule.For<Config>().FromEnvironment("APP_")           // Always returns object
rule.For<Config>().FromHttp("http://api")            // Always returns object
```

**2. Predictability**
```csharp
var config = manager.GetConfig<Config>();
// Never null if rule is defined
// C# property defaults always present (even on first failure)
```

**3. True "Last Known Good" Semantics**
```csharp
// Before: First failure → null (no history), second failure → last good value
// After: Always has value (empty object with C# defaults is the baseline)

// Optional file rule fails on startup:
rule.For<Config>().FromFile("config.json")
// OLD: null (no last good) → app must handle null
// NEW: Config with C# defaults → app always works, health shows Degraded

// File becomes available later → reactive update merges over defaults
// File fails again → keeps last merged value (true last known good)
```

**4. No Hacks Needed**
```csharp
// Before: Hack to get empty object
rule.For<Config>().FromFile("config.json"),
rule.For<Config>().FromEnvironment("FAKE_PREFIX_")  // ❌ Unclear intent

// After: Explicit if you want custom defaults
rule.For<Config>().FromStatic(_ => new Config { /* custom defaults */ }),
rule.For<Config>().FromFile("config.json")  // ✅ Clear intent
```

**5. Better Observability**
```csharp
// Data still flows (Config has C# defaults), but health reflects the real issue.
// Overall status is derived from per-rule outcomes by the health tracker:
manager.HealthStatus;  // HealthStatus.Degraded — an optional rule failed
manager.IsHealthy;     // false

// Per-rule detail is tracked on the rule manager:
//   LastOutcome           → RuleExecutionOutcome.Failed
//   LastFailureException  → FileNotFoundException("config.json")
```

**6. Graceful Degradation**
```csharp
// App continues with defaults while source is unavailable
// Auto-recovers when source comes back online (reactive updates)
// "Last known good" becomes meaningful: empty object → first merge → subsequent merges
```

### Implementation Approach

**Change in RuleManager:**

```csharp
// Before:
private ReadOnlyMemory<byte> HandleFailure(Exception ex)
{
    LastOutcome = RuleExecutionOutcome.Failed;
    LastFailureException = ex;

    if (Required)
    {
        throw new InvalidOperationException(...);
    }

    _logger.OptionalRuleFailed(ex, ...);
    // ❌ Skipped the rule's contribution → type may be absent → null
}

// After:
private ReadOnlyMemory<byte> HandleFailure(Exception ex)
{
    LastOutcome = RuleExecutionOutcome.Failed;
    LastFailureException = ex;  // ✅ Still tracked for health

    if (Required)
    {
        throw new InvalidOperationException(...);
    }

    _logger.OptionalRuleFailed(ex, ...);
    return EmptyObjectResult();  // ✅ Contributes "{}"u8 → object with C# defaults
}
```

**Similarly for HandleSelectFailure** (when Select path missing on optional rules).

**Important distinction for `Select(...)` paths:**
- **Required rules**: Missing Select path still causes hard failure and rolls back entire recompute to last known good (preserves required rule safety guarantees)
- **Optional rules**: Missing Select path returns `{}` and marks rule as Degraded in health

This maintains required rules as a strong guardrail against misconfiguration while giving optional rules graceful degradation.

**`include: false` Reserved for `.When()` Only:**
```csharp
rule.For<PremiumFeatures>().FromFile("premium.json")
    .When(accessor => accessor.GetRequiredConfig<TenantSettings>().IsPremium)
// When condition = false → include: false (intentional semantic skip)
// Provider not called, no health impact
```

### Impact on GetRequiredConfig

With this change, `GetRequiredConfig<T>()` throws only when:
- No rules are defined for type `T`
- Interface `T` is not exposed via `ExposeAs<T>()`

It becomes a **static configuration safety check** rather than a runtime availability check.

---

## Consequences

### Positive

✅ **Bug fixed** - All providers now behave identically (as intended)
✅ **Predictability** - Types are always available if configured
✅ **No workarounds needed** - Eliminates environment var hacks
✅ **No null checks** - Simpler consumer code
✅ **Better defaults** - C# property defaults always present
✅ **Richer health** - Separate concern from data flow
✅ **True graceful degradation** - Apps continue with defaults during failures

### Potential Impact

⚠️ **Behavioral change** - Code checking for null to detect optional rule failures will no longer see null
⚠️ **Documentation update** - PART2 article needs revision to reflect correct behavior

### Not a Breaking Change (In Practice)

Users who were working around the bug by checking for null to detect failures may need to adjust, but this is not a breaking change because:
- The documented intent was "graceful degradation" for optional rules
- Collection providers already demonstrated the correct behavior (returning `{}`)
- The null return was inconsistent and required hacky workarounds
- All 349 tests passed without modification after the fix
- No legitimate use case for "optional rule returns null" that isn't better served by health monitoring

### Migration (If Needed)

Replace null checks (which were a workaround for the bug) with the proper health API:

```csharp
var config = manager.GetConfig<OptionalConfig>();
UseConfig(config);  // Always works, may have defaults

// Proper way to check if the configuration is healthy:
if (!manager.IsHealthy)
{
    // manager.HealthStatus is Degraded when an optional rule failed.
    // Per-rule detail (LastOutcome, LastFailureException) is exposed through
    // the rule managers for diagnostics and ConfigHub observability.
    _logger.LogWarning("Configuration is degraded: {Status}", manager.HealthStatus);
}
```

**Note:** Most code won't need changes - checking for null was a workaround for the bug, and most users either:
1. Used DI injection (never saw null)
2. Used the config directly (relied on defaults)
3. Had workarounds like adding `FromEnvironment("FAKE_")` rules (no longer needed)

### Testing Impact

Existing tests checking for `null` from optional rules will need updates:

```csharp
// Before:
var result = manager.GetConfig<TestConfig>();
Assert.Null(result);  // File doesn't exist

// After:
var result = manager.GetConfig<TestConfig>();
Assert.NotNull(result);  // Returns empty object
Assert.Equal(default, result.SomeProperty);  // C# defaults present

// Check health instead:
Assert.Equal(HealthStatus.Degraded, manager.HealthStatus);
Assert.False(manager.IsHealthy);
```

---

## Alternatives Considered

### 1. Keep Current Behavior, Document FromStatic Pattern

**Decision:** Reject
**Reason:** Doesn't solve the EnvironmentVariable workaround hack, maintains inconsistency

### 2. Make Providers Return `null` or `{}`

**Decision:** Reject
**Reason:** Provider API becomes ambiguous, mixes concerns

### 3. Change Only FileSourceProvider to Return `{}`

**Decision:** Reject
**Reason:** Partial solution, doesn't address root cause

### 4. Add `.WithDefaults()` Fluent API

```csharp
rule.For<Config>().FromFile("config.json")
    .WithDefaults(new Config { /* ... */ })
```

**Decision:** Defer
**Reason:** Could be added later as enhancement, doesn't solve core consistency issue

---

## Related Issues

- **Original bug report:** Single optional rule (FromFile) with missing file returns null inconsistently
- **Workaround that revealed the bug:** Adding FromEnvironment with fake prefix creates empty object (exposing that collection providers were already correct)
- **Design principle:** Collection providers (Env/CLI) already demonstrated the correct behavior

---

## References

- `.local/dev.to/PART2-config-aware-rules-in-net-the-power-feature-of-cocoarconfiguration-part-2.md` (Lines 46-85, 175-200)
- `src/Cocoar.Configuration/Rules/RuleManager.cs` (HandleFailure, HandleSelectFailure methods)
- `src/Cocoar.Configuration/Providers/` (Provider implementations)

---

## Notes

This ADR documents a **bug fix** that corrects inconsistent provider behavior. While framed as an architectural decision, it's fundamentally fixing a defect where source-based providers (File, HTTP) had different failure semantics than collection-based providers (Environment, CommandLine).

The key insight: **Separation of concerns** - data flow (always flows) vs health monitoring (tracks issues separately) - was always the intended design, but source-based providers weren't implementing it correctly.
