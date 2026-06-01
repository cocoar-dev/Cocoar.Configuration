---
description: Incremental IOptions/IConfiguration migration — FromIConfiguration bridge, IOptionsMonitor to IReactiveConfig, PostConfigure as last-write-wins rule, mapping table
---

# Migrating from IOptions

This guide walks you through migrating an existing ASP.NET Core application from Microsoft's `IOptions<T>` / `IConfiguration` to Cocoar.Configuration. The migration is **incremental** -- you can move one type at a time, and both systems run side by side throughout the process.

## Why Migrate?

| Microsoft Pain Point | Cocoar Equivalent | Benefit |
|---|---|---|
| `IOptions<T>` requires `.Value` unwrapping | Inject `T` directly | Less ceremony, cleaner constructors |
| `IOptionsSnapshot<T>` for per-request | `T` is Scoped by default | Same behavior, no wrapper |
| `IOptionsMonitor<T>` + `.OnChange()` | `IReactiveConfig<T>` + `.Subscribe()` | Standard `IObservable<T>` semantics |
| No atomic multi-config updates | `IReactiveConfig<(T1, T2)>` | Consistent reads guaranteed |
| Manual `Configure<T>()` per type | Declarative rules with layering | One place defines all sources |

For a deeper comparison, see [Why Cocoar?](/guide/why-cocoar).

## Step 1: Install & Coexist

Add the Cocoar packages alongside your existing Microsoft configuration. **Nothing changes for existing code.**

```shell
dotnet add package Cocoar.Configuration.DI
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```

Then register Cocoar in `Program.cs`, bridging from the `IConfiguration` you already have:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Your existing Microsoft configuration still works.
// builder.Configuration already has appsettings.json, env vars, etc.

// Add Cocoar — reads from the SAME IConfiguration
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromIConfiguration(builder.Configuration).Select("App"),
        rules.For<DatabaseConfig>().FromIConfiguration(builder.Configuration).Select("Database")
    ]));
```

At this point both systems are active. Old services using `IOptions<AppSettings>` keep working. New services can inject `AppSettings` directly. No conflicts, no breaking changes.

::: tip
`FromIConfiguration` watches `IConfiguration.GetReloadToken()`, so file-change notifications flow through automatically. See [Microsoft IConfiguration Adapter](/guide/providers/microsoft-adapter) for details.
:::

## Step 2: Migrate Services (Type by Type)

Pick a single service and swap the wrapper for direct injection. You do not need to migrate everything at once.

**Before:**

```csharp
public class OrderService(IOptions<AppSettings> options)
{
    public void Process()
    {
        var settings = options.Value;
        // use settings...
    }
}
```

**After:**

```csharp
public class OrderService(AppSettings settings)
{
    public void Process()
    {
        // Just use settings directly — no .Value unwrapping
    }
}
```

Both injection patterns work at the same time in the same DI container. Migrate one service, run your tests, then move to the next.

::: info
Start with **leaf services** -- services that don't inject other config-dependent services. These are the simplest to migrate and verify in isolation.
:::

## Step 3: Upgrade to Reactive (Where Needed)

For services that used `IOptionsMonitor<T>` to react to configuration changes at runtime, switch to `IReactiveConfig<T>`:

**Before:**

```csharp
public class CacheService : IDisposable
{
    private readonly IDisposable _subscription;

    public CacheService(IOptionsMonitor<CacheSettings> monitor)
    {
        _subscription = monitor.OnChange(settings => RebuildCache(settings));
    }

    public void Dispose() => _subscription.Dispose();
}
```

**After:**

```csharp
public class CacheService : IDisposable
{
    private readonly IDisposable _subscription;

    public CacheService(IReactiveConfig<CacheSettings> config)
    {
        _subscription = config.Subscribe(settings => RebuildCache(settings));
    }

    public void Dispose() => _subscription.Dispose();
}
```

`IReactiveConfig<T>` implements `IObservable<T>`, so you get standard Rx semantics -- including the ability to combine multiple configs atomically with [reactive tuples](/guide/reactive/tuples).

## Step 4: Switch to Native Providers (Optional)

Once your services are migrated, you can optionally replace the `FromIConfiguration` bridge with native Cocoar providers. This gives you full reactive support, better performance, and no dependency on the Microsoft configuration pipeline.

```csharp
// Before: bridged through Microsoft
rules.For<AppSettings>()
    .FromIConfiguration(builder.Configuration)
    .Select("App")

// After: native Cocoar providers with full reactive support
rules.For<AppSettings>()
    .FromFile("appsettings.json").Select("App"),
rules.For<AppSettings>()
    .FromEnvironment("APP_"),
```

This step is entirely optional. `FromIConfiguration` works correctly long-term and there is no pressure to remove it.

## Mapping Table

| Microsoft | Cocoar | Notes |
|---|---|---|
| `IOptions<T>` | `T` (inject directly) | No wrapper needed |
| `IOptionsSnapshot<T>` | `T` (Scoped) | Same behavior -- stable per request |
| `IOptionsMonitor<T>` | `IReactiveConfig<T>` | `.Subscribe()` instead of `.OnChange()` |
| `Configure<T>(section)` | `rules.For<T>().FromIConfiguration(config).Select(section)` | Declarative rules |
| `builder.Configuration.GetSection("X")` | `.Select("X")` | Standard Cocoar pattern |
| `PostConfigure<T>()` | Additional rule (last-write-wins) | [Layering](/guide/configuration/rules) handles this naturally |
| `ValidateDataAnnotations()` | `.Required()` + C# validation | `.Required()` ensures the source exists. For property validation (`[Range]`, `[Url]`, etc.), use standard C# validation in your config class constructor or a factory method. Cocoar does not run Data Annotation validators automatically. |

### PostConfigure example

The `PostConfigure<T>()` pattern translates to an additional rule. Because Cocoar uses last-write-wins layering, a second rule for the same type overrides specific properties:

```csharp
// Microsoft: PostConfigure overrides values after Configure
services.Configure<AppSettings>(config.GetSection("App"));
services.PostConfigure<AppSettings>(s => s.MaxRetries = 10);

// Cocoar: additional rule — last write wins
rules.For<AppSettings>().FromIConfiguration(builder.Configuration).Select("App"),
rules.For<AppSettings>().FromStaticJson("""{ "MaxRetries": 10 }"""),
```

## Tips

- **Start with leaf services.** Services that don't inject other config types are the easiest to migrate and verify.
- **Don't remove `Configure<T>()` calls** until every consumer of that type has been migrated to direct injection.
- **Both registration systems coexist.** Microsoft DI handles both `IOptions<T>` and direct `T` injection simultaneously with no conflicts.
- **Tests:** Use `CocoarTestConfiguration.ReplaceConfiguration()` for migrated services. Existing services keep using their current test patterns. See [Test Overrides](/guide/testing/overrides) for details.
- **Gradual is fine.** There is no deadline to finish the migration. A codebase that uses both systems works correctly and is fully supported.
