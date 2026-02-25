# Migration Guide: v4.x → v5.0

This guide explains how to migrate from v4.x to v5.0, which introduces the **ConfigManager Builder API** — a single entry point for creating fully-initialized ConfigManager instances.

## Overview

v5.0 introduces two major breaking changes:

1. **`ConfigManager` constructors and `Initialize()` are now `internal`** — use `ConfigManager.Create()` instead
2. **`AddCocoarConfiguration()` now uses the builder API** — use `AddCocoarConfiguration(c => c.WithConfiguration(...))` instead of `AddCocoarConfiguration(rule => [...])`

### Migration Table

| v4.x API | v5.0 API |
|----------|----------|
| `new ConfigManager(rules).Initialize()` | `ConfigManager.Create(c => c.WithConfiguration(rules))` |
| `new ConfigManager(rules, setup).Initialize()` | `ConfigManager.Create(c => c.WithConfiguration(rules, setup))` |
| `new ConfigManager(rules, logger: l).Initialize()` | `ConfigManager.Create(c => c.WithConfiguration(rules).UseLogger(l))` |
| `new ConfigManager(rules, debounceMilliseconds: 50).Initialize()` | `ConfigManager.Create(c => c.WithConfiguration(rules).UseDebounce(50))` |
| `services.AddCocoarConfiguration(rule => [...])` | `services.AddCocoarConfiguration(c => c.WithConfiguration(rule => [...]))` |
| `services.AddCocoarConfiguration(rule => [...], setup => [...])` | `services.AddCocoarConfiguration(c => c.WithConfiguration(rule => [...], setup => [...]))` |
| `builder.AddCocoarConfiguration(rule => [...])` | `builder.AddCocoarConfiguration(c => c.WithConfiguration(rule => [...]))` |

## Why the Change?

The old API had two problems:

1. **Split construction/initialization** — `new ConfigManager(...)` created an uninitialized object, requiring a separate `.Initialize()` call. Forgetting `Initialize()` led to subtle bugs.
2. **Mixed concerns in the constructor** — Rules, setup, logger, debounce, and provider factory were all positional/optional parameters on one constructor.

The new API solves both:
- `ConfigManager.Create()` returns a **fully-initialized** instance — no `Initialize()` needed
- The builder groups concerns logically: `.WithConfiguration()` for rules/setup, `.UseLogger()` for logging, etc.
- The builder provides extension points (`.AfterBuild()`) for satellite libraries

## Quick Examples

### Basic Rules

**Before (v4.x):**
```csharp
var manager = new ConfigManager(rule => [
    rule.For<AppSettings>().FromFile("config.json"),
    rule.For<DbSettings>().FromEnvironment("DB_")
]).Initialize();
```

**After (v5.0):**
```csharp
var manager = ConfigManager.Create(c => c
    .WithConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json"),
        rule.For<DbSettings>().FromEnvironment("DB_")
    ]));
```

### Rules with Setup

**Before (v4.x):**
```csharp
var manager = new ConfigManager(
    rule => [
        rule.For<AppSettings>().FromFile("config.json")
    ],
    setup => [
        setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
    ]
).Initialize();
```

**After (v5.0):**
```csharp
var manager = ConfigManager.Create(c => c
    .WithConfiguration(
        rule => [
            rule.For<AppSettings>().FromFile("config.json")
        ],
        setup => [
            setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
        ]));
```

### With Logger and Debounce

**Before (v4.x):**
```csharp
var manager = new ConfigManager(
    rule => [rule.For<AppSettings>().FromFile("config.json")],
    logger: myLogger,
    debounceMilliseconds: 50
).Initialize();
```

**After (v5.0):**
```csharp
var manager = ConfigManager.Create(c => c
    .WithConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json")
    ])
    .UseLogger(myLogger)
    .UseDebounce(50));
```

### Pre-built Rules (IEnumerable)

**Before (v4.x):**
```csharp
var rules = new[] { rule1, rule2, rule3 };
var manager = new ConfigManager(rules, logger: NullLogger.Instance).Initialize();
```

**After (v5.0):**
```csharp
var rules = new[] { rule1, rule2, rule3 };
var manager = ConfigManager.Create(c => c
    .WithConfiguration(rules)
    .UseLogger(NullLogger.Instance));
```

### Secrets Configuration

**Before (v4.x):**
```csharp
var manager = new ConfigManager(
    rule => [rule.For<AppConfig>().FromFile("config.json")],
    setup => [
        setup.Secrets()
            .UseCertificateFromFile("secrets.pfx")
            .WithKeyId("dev-secrets")
    ]
).Initialize();
```

**After (v5.0):**
```csharp
var manager = ConfigManager.Create(c => c
    .WithConfiguration(
        rule => [rule.For<AppConfig>().FromFile("config.json")])
    .WithSecretsSetup(secrets => secrets
        .UseCertificateFromFile("secrets.pfx")
        .WithKeyId("dev-secrets")));
```

> **Note:** Secrets setup is now a dedicated extension method `WithSecretsSetup()` on the builder,
> rather than part of the `setup` lambda. This provides better separation of concerns.

## DI and ASP.NET Core

`AddCocoarConfiguration()` now uses the same builder API as `ConfigManager.Create()`:

**Before (v4.x):**
```csharp
services.AddCocoarConfiguration(
    rule => [rule.For<AppSettings>().FromFile("config.json")],
    setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);
```

**After (v5.0):**
```csharp
services.AddCocoarConfiguration(c => c
    .WithConfiguration(
        rule => [rule.For<AppSettings>().FromFile("config.json")],
        setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]));
```

**ASP.NET Core:**
```csharp
builder.AddCocoarConfiguration(c => c
    .WithConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json")
    ]));
```

**With Secrets:**
```csharp
services.AddCocoarConfiguration(c => c
    .WithConfiguration(rule => [
        rule.For<AppConfig>().FromFile("config.json")
    ])
    .WithSecretsSetup(secrets => secrets
        .UseCertificateFromFile("secrets.pfx")
        .WithKeyId("dev-secrets")));
```

### Pre-built ConfigManager

You can still pass a pre-built `ConfigManager` instance:

```csharp
// Before:
var manager = new ConfigManager(rule => [...]).Initialize();
services.AddCocoarConfiguration(manager);

// After:
var manager = ConfigManager.Create(c => c.WithConfiguration(rule => [...]));
services.AddCocoarConfiguration(manager);
```

## Automated Migration

For most cases, find/replace works:

### Pattern 1: Chained Initialize
**Find:** `new ConfigManager(rule => [`
**Replace with:** `ConfigManager.Create(c => c.WithConfiguration(rule => [`

Then replace the closing `).Initialize()` with `))`.

### Pattern 2: Separate Initialize
Remove the `.Initialize()` call and wrap the constructor in `ConfigManager.Create(c => c.WithConfiguration(...))`.

## What Stays the Same

- Rule building API: `rule.For<T>().FromFile(...)`, `.Select()`, `.Required()`, `.When()`, etc.
- Setup builder API: `setup.ConcreteType<T>().ExposeAs<I>()`
- `AddCocoarConfiguration()` extension methods (now use builder API)
- Reactive configuration: `IReactiveConfig<T>`
- Health monitoring
- Testing overrides: `CocoarTestConfiguration`
- All provider implementations

## Need Help?

- Check the [examples](../src/Examples/) — all updated to v5.0
- Review the [test suite](../src/tests/) for real-world patterns
- See the [API documentation](../README.md) for current patterns
