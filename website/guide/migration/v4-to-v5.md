---
description: v4 to v5 — ConfigManager.Create builder API, 10+ packages consolidated to 7, feature flags & entitlements, HttpPolling renamed to Http, Flag<T> to FeatureFlag<T>, health and resolver API renames
---

# Migration v4 → v5

v5.0 introduces the **ConfigManager Builder API**, **package consolidation** (10+ packages → 7), **feature flags & entitlements**, **HTTP provider rename**, and several **API renames**.

## Breaking Changes

1. **`ConfigManager` constructors and `Initialize()` are now `internal`** — use `ConfigManager.Create()` instead
2. **`AddCocoarConfiguration()` uses the builder API** — wrap rules in `c => c.UseConfiguration(...)`
3. **Secrets setup moved** from the `setup` lambda to a dedicated `.UseSecretsSetup()` builder method
4. **Testing API renamed** — `ReplaceAllRules()` → `ReplaceConfiguration()`, `AppendTestRules()` → `AppendConfiguration()`
5. **Package renamed** — `Cocoar.Configuration.HttpPolling` → `Cocoar.Configuration.Http`
6. **`Flag<T>` renamed** to `FeatureFlag<T>`
7. **`FromHttpPolling()` renamed** to `FromHttp()`
8. **`FromMicrosoftSource()` deprecated** — use `FromIConfiguration()` instead
9. **Health API simplified** — `GetHealthService()` replaced by `ConfigManager.HealthStatus` / `ConfigManager.IsHealthy`
10. **Resolver registration changed** — `RegisterGlobalContextResolver()` / `WithContextResolver()` replaced by `ResolverBuilder` with collection expressions

## Package Changes

```bash
# Remove old/merged packages
dotnet remove package Cocoar.Configuration.Secrets
dotnet remove package Cocoar.Configuration.Secrets.Abstractions
dotnet remove package Cocoar.Configuration.HttpPolling
dotnet remove package Cocoar.Configuration.Analyzers    # Now bundled in Cocoar.Configuration

# Update/add new packages
dotnet add package Cocoar.Configuration              # Now includes Secrets + Flags + Analyzers
dotnet add package Cocoar.Configuration.Abstractions  # Now includes Secrets.Abstractions
dotnet add package Cocoar.Configuration.Http          # Was HttpPolling
```

:::warning Remove Cocoar.Configuration.Analyzers
The analyzers and source generator are now bundled inside the `Cocoar.Configuration` package. If you keep a separate `Cocoar.Configuration.Analyzers` PackageReference, you will get **duplicate type errors** (CS0101/CS0102) because the source generator runs twice. Remove the separate reference.
:::

Same types, same namespaces — just fewer packages to install.

| v4.x Package | v5.0 |
|---|---|
| `Cocoar.Configuration.Secrets` | Merged into `Cocoar.Configuration` |
| `Cocoar.Configuration.X509Encryption` | Merged into `Cocoar.Configuration` |
| `Cocoar.Configuration.Flags` | Merged into `Cocoar.Configuration` |
| `Cocoar.Configuration.Flags.Generator` | Merged into `Cocoar.Configuration.Analyzers` |
| `Cocoar.Configuration.Secrets.Abstractions` | Merged into `Cocoar.Configuration.Abstractions` |
| `Cocoar.Configuration.HttpPolling` | Renamed to `Cocoar.Configuration.Http` |

## API Renames

| v4.x | v5.0 |
|---|---|
| `Flag<T>` | `FeatureFlag<T>` |
| `Flag<TContext, TResult>` | `FeatureFlag<TContext, TResult>` |
| `FromHttpPolling(...)` | `FromHttp(url, ...)` |
| `FromMicrosoftSource(...)` | `FromIConfiguration(config)` |
| `HttpPollingRuleOptions` | `HttpRuleOptions` |
| `manager.GetHealthService()` | `manager.HealthStatus` / `manager.IsHealthy` |
| `IConfigurationHealthService` | Removed — use `ConfigManager.HealthStatus` directly |
| `FeatureFlagsSetupBuilder` | `FlagsBuilder` |
| `FlagClassRegistrationBuilder` | `FlagsBuilder` |
| `EntitlementClassRegistrationBuilder` | `EntitlementsBuilder` |
| `RegisterGlobalContextResolver<T>()` | `resolvers.Global<T>()` |
| `WithContextResolver<T>()` | `resolvers.For<T>(r => r.Use<T>())` |

## Namespace Changes

```csharp
// v4.x
using Cocoar.Configuration.HttpPolling;

// v5.0
using Cocoar.Configuration.Http;
```

## Migration Table

| v4.x | v5.0 |
|---|---|
| `new ConfigManager(rules).Initialize()` | `ConfigManager.Create(c => c.UseConfiguration(rules))` |
| `new ConfigManager(rules, setup).Initialize()` | `ConfigManager.Create(c => c.UseConfiguration(rules, setup))` |
| `new ConfigManager(rules, logger: l).Initialize()` | `ConfigManager.Create(c => c.UseConfiguration(rules).UseLogger(l))` |
| `new ConfigManager(rules, debounceMilliseconds: 50).Initialize()` | `ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50))` |
| `services.AddCocoarConfiguration(rule => [...])` | `services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [...]))` |
| `services.AddCocoarConfiguration(rule => [...], setup => [...])` | `services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [...], setup => [...]))` |
| `builder.AddCocoarConfiguration(rule => [...])` | `builder.AddCocoarConfiguration(c => c.UseConfiguration(rule => [...]))` |

## ConfigManager.Create()

The old API split construction and initialization — `new ConfigManager(...)` created an uninitialized object requiring a separate `.Initialize()`. Forgetting `Initialize()` caused subtle bugs.

The new API returns a fully-initialized instance. No `Initialize()` needed.

### Basic Rules

```csharp
// v4.x
var manager = new ConfigManager(rule => [
    rule.For<AppSettings>().FromFile("config.json"),
    rule.For<DbSettings>().FromEnvironment("DB_")
]).Initialize();

// v5.0
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json"),
        rule.For<DbSettings>().FromEnvironment("DB_")
    ]));
```

### Rules with Setup

```csharp
// v4.x
var manager = new ConfigManager(
    rule => [rule.For<AppSettings>().FromFile("config.json")],
    setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]
).Initialize();

// v5.0
var manager = ConfigManager.Create(c => c
    .UseConfiguration(
        rule => [rule.For<AppSettings>().FromFile("config.json")],
        setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]));
```

### With Logger and Debounce

```csharp
// v4.x
var manager = new ConfigManager(
    rule => [rule.For<AppSettings>().FromFile("config.json")],
    logger: myLogger,
    debounceMilliseconds: 50
).Initialize();

// v5.0
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json")
    ])
    .UseLogger(myLogger)
    .UseDebounce(50));
```

### Async Initialization (New)

v5.0 adds `CreateAsync()` for scenarios where blocking during provider I/O is undesirable:

```csharp
var manager = await ConfigManager.CreateAsync(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("config.json")
    ]),
    cancellationToken);
```

## DI and ASP.NET Core

`AddCocoarConfiguration()` uses the same builder API:

```csharp
// v4.x
services.AddCocoarConfiguration(
    rule => [rule.For<AppSettings>().FromFile("config.json")],
    setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

// v5.0
services.AddCocoarConfiguration(c => c
    .UseConfiguration(
        rule => [rule.For<AppSettings>().FromFile("config.json")],
        setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]));
```

**ASP.NET Core:**
```csharp
// v4.x
builder.AddCocoarConfiguration(rule => [...]);

// v5.0
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [...]));
```

## Secrets Setup

Secrets configuration moved from the `setup` lambda to a dedicated builder method:

```csharp
// v4.x
var manager = new ConfigManager(
    rule => [rule.For<AppConfig>().FromFile("config.json")],
    setup => [
        setup.Secrets()
            .UseCertificateFromFile("secrets.pfx")
            .WithKeyId("dev-secrets")
    ]
).Initialize();

// v5.0
var manager = ConfigManager.Create(c => c
    .UseConfiguration(
        rule => [rule.For<AppConfig>().FromFile("config.json")])
    .UseSecretsSetup(secrets => secrets
        .UseCertificateFromFile("secrets.pfx")
        .WithKeyId("dev-secrets")));
```

## HTTP Provider (was HttpPolling)

The package is renamed and the API simplified:

```csharp
// v4.x
using Cocoar.Configuration.HttpPolling;

rule.For<RemoteConfig>().FromHttpPolling(accessor =>
    HttpPollingRuleOptions.FromPath("https://api.example.com/config",
        pollInterval: TimeSpan.FromSeconds(30)))

// v5.0
using Cocoar.Configuration.Http;

rule.For<RemoteConfig>().FromHttp(
    "https://api.example.com/config",
    pollInterval: TimeSpan.FromSeconds(30))
```

**New: SSE (Server-Sent Events) mode:**
```csharp
rule.For<RemoteConfig>().FromHttp(
    "https://api.example.com/config",
    serverSentEvents: true)
```

**New: One-time fetch (no polling, no SSE):**
```csharp
rule.For<RemoteConfig>().FromHttp("https://api.example.com/config")
```

## Microsoft Adapter

`FromMicrosoftSource()` is deprecated. Use the simpler `FromIConfiguration()`:

```csharp
// v4.x
rule.For<AppSettings>().FromMicrosoftSource(accessor =>
    MicrosoftConfigurationSourceRuleOptions.From(configuration))

// v5.0
rule.For<AppSettings>().FromIConfiguration(configuration)
```

## Health API

Health access simplified from a service to direct properties:

```csharp
// v4.x
var healthService = manager.GetHealthService();
var snapshot = healthService.Snapshot;
var status = snapshot.Status;

// v5.0
var status = manager.HealthStatus;
var isHealthy = manager.IsHealthy;
```

## Feature Flags Registration

The registration API uses collection expressions with `FlagsBuilder` / `EntitlementsBuilder`:

```csharp
// v4.x (FeatureFlagsSetupBuilder)
.UseFeatureFlags(f => f
    .RegisterClass<AppFeatureFlags>()
    .RegisterClass<BetaFlags>())

// v5.0 (FlagsBuilder with collection expressions)
.UseFeatureFlags(flags => [
    flags.Register<AppFeatureFlags>(),
    flags.Register<BetaFlags>()
])
```

`Flag<T>` properties are renamed to `FeatureFlag<T>`:

```csharp
// v4.x
public class AppFeatureFlags : FeatureFlags
{
    public Flag<bool> DarkMode { get; set; } = () => false;
}

// v5.0
public partial class AppFeatureFlags : IFeatureFlags<AppConfig>
{
    public FeatureFlag<bool> DarkMode => () => Config.DarkModeEnabled;
}
```

## Resolver Registration

Context resolvers now use `ResolverBuilder` with collection expressions, registered via a second parameter:

```csharp
// v4.x
.UseFeatureFlags(f => f
    .RegisterClass<BillingFlags>()
    .RegisterGlobalContextResolver<UserByIdResolver>()
    .WithContextResolver<BillingFlags, BillingResolver>())

// v5.0
.UseFeatureFlags(
    flags => [
        flags.Register<BillingFlags>()
    ],
    resolvers => [
        resolvers.Global<UserByIdResolver>(),
        resolvers.For<BillingFlags>(r => r
            .Use<BillingResolver>()
            .ForProperty(f => f.BetaCheckout).Use<BetaResolver>())
    ])
```

The default resolver lifetime changed from Transient to Scoped. Customize with `.AsSingleton()` or `.AsTransient()`.

## Testing API

Method names changed and the API is now fluent and per-concern:

| v4.x | v5.0 |
|---|---|
| `CocoarTestConfiguration.ReplaceAllRules(rule => [...])` | `CocoarTestConfiguration.ReplaceConfiguration(rule => [...])` |
| `CocoarTestConfiguration.AppendTestRules(rule => [...])` | `CocoarTestConfiguration.AppendConfiguration(rule => [...])` |
| `CocoarTestConfiguration.WithSetup(setup => [...])` | Removed — use `ReplaceSecretsSetup()` on the builder |

```csharp
// v4.x
using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
    rule.For<DbConfig>().FromStatic(_ => testDbConfig)
]);

// v5.0
using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
    rule.For<DbConfig>().FromStatic(_ => testDbConfig)
]);
```

**With secrets override (v5.0):**
```csharp
using var _ = CocoarTestConfiguration
    .ReplaceConfiguration(rule => [
        rule.For<DbConfig>().FromStatic(_ => testDbConfig)
    ])
    .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());
```

Each concern (configuration, secrets) is independent — chain freely on the returned `TestOverrideBuilder`.

## Automated Migration

For most cases, find/replace works:

**Pattern 1:** Replace `new ConfigManager(rule => [` with `ConfigManager.Create(c => c.UseConfiguration(rule => [`, then replace the closing `).Initialize()` with `))`.

**Pattern 2:** Replace `services.AddCocoarConfiguration(rule =>` with `services.AddCocoarConfiguration(c => c.UseConfiguration(rule =>`, and add the closing `)` before the final `)`.

**Pattern 3:** Replace `Flag<` with `FeatureFlag<` in flag class definitions.

**Pattern 4:** Replace `using Cocoar.Configuration.HttpPolling` with `using Cocoar.Configuration.Http`.

**Pattern 5:** Replace `.FromHttpPolling(` with `.FromHttp(` and update the options pattern to use the simplified parameters.

## What Stays the Same

- Rule building API: `rule.For<T>().FromFile(...)`, `.Select()`, `.Required()`, `.When()`
- Setup builder API: `setup.ConcreteType<T>().ExposeAs<I>()`
- Reactive configuration: `IReactiveConfig<T>`
- All provider implementations (File, Environment, CommandLine, Static, Observable)

## New in v5.0

These are purely additive — no migration needed:

- **.NET 8 LTS support** — all library packages now multi-target `net8.0` and `net9.0`, so v5 works on both .NET 8 and .NET 9
- **[Feature Flags & Entitlements](/guide/flags/concepts)** — strongly-typed, computed feature flags and entitlements built into the core package
- **`ConfigManager.CreateAsync()`** — async factory for non-blocking initialization
- **Zero external dependencies** — `System.Reactive` removed from all shipped packages
- **Runtime recomputes are fully async** — no more sync-over-async in the recompute pipeline
- **SSE support** — `FromHttp(url, serverSentEvents: true)` for live push updates
- **OpenTelemetry metrics** — `cocoar.config.recompute.count`, `cocoar.config.recompute.duration`, `cocoar.config.provider.errors`, `cocoar.config.flags.evaluations`, `cocoar.config.health.status`
- **Distributed tracing** — Activity source `Cocoar.Configuration`
- **ASP.NET Core health check** — `AddCocoarConfigurationHealthCheck()`
- **REST evaluation endpoints** — `MapFeatureFlagEndpoints()`, `MapEntitlementEndpoints()`
- **[Aggregate Rules](/guide/configuration/aggregate-rules)** — `FromFiles()` for file layering, `.Aggregate()` for general-purpose rule grouping with isolated error handling
- **VitePress documentation site** with complete guide, reference, and roadmap
