# COCFG003 — Required Rule Configuration Validation

## What it detects

A required rule (`.Required()`) is configured with a provider or path that is known to
be missing or invalid at analysis time (e.g., a literal file path that does not exist).

## Why it matters

Required rules cause `ConfigManager.Create()` to throw if they fail. A misconfigured
required rule will prevent the application from starting. This analyzer catches obvious
configuration errors at compile time.

## Example

### Non-compliant
```csharp
// Hard-coded path that doesn't exist — fails at startup
rules.For<AppSettings>().FromFile("C:/nonexistent/config.json").Required()
```

### Compliant
```csharp
rules.For<AppSettings>().FromFile("appsettings.json").Required()
// Or use Optional() for paths that may not exist
rules.For<AppSettings>().FromFile("appsettings.local.json").Optional()
```

## How to fix

Verify the provider path is correct, or change `.Required()` to `.Optional()` for
configuration files that are not guaranteed to exist in all environments.
