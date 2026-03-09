# COCFG002 — Rule Dependency Ordering Violation

## What it detects

A configuration rule references a type from an earlier rule using `GetConfig<T>()` inside
a provider or query factory, but the dependent rule appears *before* the rule it depends on
in the rule list.

## Why it matters

Rules execute in order. If Rule B's factory calls `GetConfig<T>()` to read a value produced
by Rule A, Rule A must appear before Rule B. Violating this order means Rule B reads stale
or default values during initialization.

## Example

### Non-compliant
```csharp
// DbConfig depends on AppConfig, but appears first
rules.For<DbConfig>().FromFile(cm => cm.GetConfig<AppConfig>()!.DbConfigPath),
rules.For<AppConfig>().FromFile("appsettings.json")
```

### Compliant
```csharp
rules.For<AppConfig>().FromFile("appsettings.json"),
rules.For<DbConfig>().FromFile(cm => cm.GetConfig<AppConfig>()!.DbConfigPath)
```

## How to fix

Reorder rules so that each rule's dependencies appear before it in the rule list.
