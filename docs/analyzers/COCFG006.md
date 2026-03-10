# COCFG006 — Static Provider Ordering Suggestion

## What it detects

A `FromStatic()` rule appears after a `FromFile()` or `FromEnvironment()` rule for the same
type. Static providers are evaluated at configuration time and override dynamic providers.
Placing a static provider last can mask changes from dynamic providers.

## Why it matters

`FromStatic()` values never change at runtime. If a static rule overwrites a file-based rule's
output, hot-reload changes to the file will be silently ignored for the overwritten fields.

## Example

### Non-compliant
```csharp
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>().FromStatic(_ => new AppSettings { LogLevel = "Debug" })
// The static value always wins — file changes to LogLevel are ignored
```

### Compliant
```csharp
// Put static overrides before dynamic sources so dynamic sources can override them
rules.For<AppSettings>().FromStatic(_ => new AppSettings { LogLevel = "Info" }),
rules.For<AppSettings>().FromFile("appsettings.json") // file can override the default
```

## How to fix

Move `FromStatic()` rules before dynamic provider rules for the same type, so that dynamic
sources can override the static defaults.
