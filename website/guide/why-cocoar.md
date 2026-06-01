---
description: Cocoar versus IOptions — direct injection, ordered rule layering, IReactiveConfig updates, atomic multi-config tuples, required rollback, built-in flags and secrets
---

# Why Cocoar.Configuration?

## The Problem with IOptions

Microsoft's `IConfiguration` and `IOptions<T>` work, but they come with friction:

```csharp
// Microsoft: Setup is ceremony
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));

// Microsoft: Injection requires unwrapping
public class MyService(IOptions<AppSettings> options)
{
    var settings = options.Value; // Unwrap every time
}
```

- You must remember `Configure<T>()` for every type
- Consumers need `IOptions<T>`, `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>` — different wrappers for different lifetimes
- Layering multiple sources (file + environment + remote) requires manual `IConfigurationBuilder` wiring
- No atomic multi-config updates — if two config types need to change together, you can get inconsistent reads
- Change notification requires subscribing to `IOptionsMonitor<T>` with manual callback management

## The Cocoar Approach

```csharp
// Cocoar: Setup is one line per type
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json").Select("App")
    ]));

// Cocoar: Inject directly — no wrapper
public class MyService(AppSettings settings)
{
    // Just use it
}
```

### What You Get

| Capability | IOptions | Cocoar |
|---|---|---|
| Direct injection | `IOptions<T>` wrapper | `T` directly |
| Layering | Manual builder wiring | Rules in order, last write wins |
| Reactive updates | `IOptionsMonitor<T>` | `IReactiveConfig<T>` |
| Atomic multi-config | Not supported | `IReactiveConfig<(T1, T2)>` |
| Conditional rules | Not supported | `.When(accessor => ...)` |
| Required vs optional | Manual validation | `.Required()` with automatic rollback |
| Health monitoring | Not built in | Per-rule status, degraded/unhealthy tracking |
| Feature flags | Separate library | Built in |
| Secrets | No memory safety | `Secret<T>` with automatic zeroization |
| Compile-time validation | Not available | Roslyn analyzers (COCFG001-006) |

### Design Principles

**Explicit layering.** Rules execute in defined order and merge property by property — later rules overlay earlier ones. You read the rule list top to bottom and know exactly what happens.

**Reactive by default.** Every configuration type automatically gets an `IReactiveConfig<T>` in DI. Subscribe once and receive updates whenever config changes — file modifications, HTTP poll results, environment changes.

**Atomic updates.** When multiple config types need to stay in sync, use `IReactiveConfig<(T1, T2, T3)>`. All types update together in one snapshot — you never see a mix of old and new values.

**Fail-safe behavior.** Required rules roll back the entire recompute on failure — your app keeps the last known good config. Optional rules that fail contribute nothing — values set by earlier rules remain unchanged, and the failure is tracked in health.

**Zero ceremony.** Define a class, add a rule, inject it. No `Configure<T>()` registration, no options wrappers, no `GetSection()` calls.
