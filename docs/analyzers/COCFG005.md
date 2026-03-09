# COCFG005 — Duplicate Unconditional Rules Detected

## What it detects

Two or more rules for the same configuration type are both unconditional (no `.When()` predicate)
and use the same provider type, resulting in redundant configuration loading.

## Why it matters

Duplicate unconditional rules waste I/O and processing. The first rule's result is entirely
overwritten by the second rule (last-write-wins). In most cases this is unintentional.

## Example

### Non-compliant
```csharp
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>().FromFile("appsettings.json") // identical — second overwrites first entirely
```

### Compliant
```csharp
// Use different files for layering
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>().FromFile("appsettings.Production.json").When(cm => IsProduction())
// Or deduplicate if the duplicate is accidental
rules.For<AppSettings>().FromFile("appsettings.json")
```

## How to fix

Remove the duplicate rule, or add a `.When()` condition to make the second rule conditional.
