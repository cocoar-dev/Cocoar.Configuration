# Reactive, strongly-typed configuration layering for .NET

![Cocoar.Configuration](social-preview-small.png)
> Elevates configuration from hidden infrastructure to an observable, safety‑enforced subsystem you can trust under change and failure.

[![Build (develop)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml/badge.svg)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml)
[![PR Validation](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml/badge.svg)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)

---

## Features

### Key Capabilities
* **Automatic Reactive Configs** – Every configured concrete type provides `IReactiveConfig<T>` (no opt-in).
* **Tuple Reactive Snapshots** – `IReactiveConfig<(T1,T2,...,TN)>` for atomic aligned multi-config state (any tuple arity) with strict validation.
* **Health Monitoring Service** – Real-time provider & rule health snapshots, status streams, metrics hook (see [`docs/health-monitoring.md`](./docs/health-monitoring.md)).
* **Streaming MD5 Change Detection** – Efficient hashing for file & HTTP providers.

### Core Principles
* **Deterministic Layering** – Ordered rules, last-write-wins, no hidden merge magic.
* **Strongly Typed** – Direct POCO access (no `IOptions<T>` ceremony).
* **Atomic Snapshots** – Always consistent; no partial updates.
* **Reactive & Dynamic** – Change propagation with debounce + coalescing.

### Rule System
* **Unified Fluent API** across providers.
* **Composable Pipeline**: `Fetch → Select → Mount → Merge`.
* **Dynamic Rule Factories** – Derive new rules from earlier snapshot state.
* **Interface Exposure** – Expose implemented interfaces for DI consumption.
* **Partial Recomputation** – Only earliest changed rule re-executes.
* **Required / Optional Rules** – Control startup fail vs graceful degradation.
* **Conditional Execution** – Enable rules only when predicates hold.

### Providers
* **Static / Observable** (in-memory) built-ins.
* **File** – Watch + resilient poll fallback & recovery.
* **Environment** – `__` / `:` hierarchy mapping.
* **HTTP** – Caching, headers, change-only emissions.
* **Microsoft Adapter** – Bridge existing `IConfiguration` (extension package).

### Health Monitoring & Reliability
* Integrated `IConfigurationHealthService`.
* Status & snapshot streams (coalesced, no duplicates).
* Two‑phase error handling: fail-fast init, graceful runtime.
* Last-known-good retention on provider failures.

### Developer Experience (Simplified)
* **Minimal Boilerplate** – Define class + rule; optionally expose interfaces.
* **Always Reactive** – `IReactiveConfig<T>` & tuple variants auto available.
* **Scoped by Default** – Concrete & exposed interfaces registered as Scoped.
* **Opt-Out** – `.DisableAutoRegistration()` (concrete) or global `setup.ExposedType<IMy>().DisableAutoRegistration()`.
* **Atomic Tuple Snapshots** – Any tuple shape; aligned updates only.
* **Test-Friendly** – Deterministic providers; easy fake sources.
* **Clean Migration Path** – Legacy Bind API replaced by Configure (see migration doc).

---

## Install

```pwsh
dotnet add package Cocoar.Configuration
# For ASP.NET Core integration:
dotnet add package Cocoar.Configuration.AspNetCore
```

---

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Select("App"),
    rule.For<AppSettings>().FromEnvironment("APP_")
], setup => [
    setup.ConcreteType<AppSettings>()
        .ExposeAs<IAppSettings>() // optional interface exposure
]);

var app = builder.Build();

// Interface injection (Scoped)
app.MapGet("/feature", (IAppSettings cfg) => new { cfg.FeatureFlag, cfg.Message });

// Reactive tuple injection
app.MapGet("/composite", (IReactiveConfig<(AppSettings App, LoggingConfig Log)> composite) =>
{
    var (appCfg, log) = composite.CurrentValue;
    return new { appCfg.Version, log.Level };
});

app.Run();
```

### Opt-Out Examples
```csharp
// Inside your configure function:
builder.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json")
], setup => [
    // Skip concrete registration
    setup.ConcreteType<AppSettings>().DisableAutoRegistration(),
    
    // Globally suppress interface registration (affects any future exposure of IAppSettings)
    setup.ExposedType<IAppSettings>().DisableAutoRegistration()
]);
```

---

## Advanced Features

### Conditional Rules with Config Awareness

The `When()` method now supports **config-aware predicates** using `IConfigurationAccessor`:

```csharp
builder.Services.AddCocoarConfiguration(rule => [
    // Load tenant info first
    rule.For<TenantSettings>().FromFile("tenant.json"),
    
    // Conditionally load premium features based on tenant tier
    rule.For<PremiumFeatures>().FromFile("premium-features.json")
        .When(accessor =>
        {
            var tenant = accessor.GetRequiredConfig<TenantSettings>();
            return tenant.Tier == "Premium";
        }),
    
    // Conditionally load based on environment variable
    rule.For<DebugSettings>().FromFile("debug-settings.json")
        .When(_ => Environment.GetEnvironmentVariable("DEBUG_MODE") == "true")
]);
```

The predicate is evaluated during initialization and on every recompute—if it returns `false`, the rule is skipped entirely. This works seamlessly with dynamic rules for powerful conditional logic.

---

## Examples

| Project | Description |
|---------|-------------|
| [BasicUsage](src/Examples/BasicUsage) | ASP.NET Core + file + environment overlay |
| [FileLayering](src/Examples/FileLayering) | Multi-file layering (base/env/local) |
| [DynamicDependencies](src/Examples/DynamicDependencies) | Rules derived from earlier snapshots |
| [ConditionalRulesExample](src/Examples/ConditionalRulesExample) | Config-aware conditional rule execution with `When()` |
| [AspNetCoreExample](src/Examples/AspNetCoreExample) | Minimal API endpoints exposing config |
| [GenericProviderAPI](src/Examples/GenericProviderAPI) | Using generic provider registration APIs |
| [HttpPollingExample](src/Examples/HttpPollingExample) | Remote HTTP polling pattern |
| [MicrosoftAdapterExample](src/Examples/MicrosoftAdapterExample) | Integrate existing `IConfigurationSource` providers |
| [StaticProviderExample](src/Examples/StaticProviderExample) | Static seeding with JSON / factories |
| [SimplifiedCoreExample](src/Examples/SimplifiedCoreExample) | Pure core (no DI) usage |
| [BindingExample](src/Examples/BindingExample) | Interface exposure without DI container |
| [TupleReactiveExample](src/Examples/TupleReactiveExample) | Tuple-based aligned reactive snapshots |

More: [Examples README](src/Examples/README.md).

---

## Quality & Reliability

Extensive automated test suite (>200 tests) spanning:
* Integration & recompute pipeline
* Concurrency, cancellation & debounce correctness
* Large JSON + high-frequency mutation stress
* File watcher ↔ polling resilience & recovery
* HTTP provider headers, caching, status handling
* Environment & adapter edge cases
* Tuple reactive alignment correctness
* Health snapshot/state transitions

See the [Testing Guide](src/tests/Cocoar.Configuration.Core.Tests/TESTING_GUIDE.md) for details.

---


## Security Notes
* Don’t commit secrets; overlay via environment / secure providers.
* Use TLS + auth for remote polling.
* Consider vault integration through the Microsoft adapter.

---

## Contributing & Versioning
* Semantic Versioning (breaking changes = MAJOR)
* Issues & PRs welcome
* Licensed under Apache 2.0 (see `LICENSE`, `NOTICE`)

### License & Trademark
This project is licensed under the [Apache License, Version 2.0](LICENSE). See [`NOTICE`](NOTICE) for attribution.

“Cocoar” and related marks are trademarks of COCOAR e.U. Usage in forks should preserve attribution and avoid implying endorsement. See [TRADEMARKS](TRADEMARKS.md).

---



