# Reactive, strongly-typed configuration layering for .NET  


![Cocoar.Configuration](social-preview-small.png)
> Elevates configuration from hidden infrastructure to an observable, safety‑enforced subsystem you can trust under change and failure.

[![Build (develop)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml/badge.svg)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml)
[![PR Validation](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml/badge.svg)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)

---

## 🌟 Features

### 🚀 Key Innovations

* **Auto-Registered Reactive Config** – Every config type gets [`IReactiveConfig<T>`](./docs/reactive-config.md) in DI automatically
* [**Health Monitoring Service**](./docs/health-monitoring.md) – Real-time health snapshots, metrics export, and resilience tracking
* **Streaming MD5 Change Detection** – High-performance hashing pipeline for file/HTTP providers

### Core Principles

* **Deterministic Layering** – Explicit ordered rules, last-write-wins, no hidden merge logic.
* **Strongly Typed** – Direct POCO injection, no `IOptions<T>` or attributes.
* **Atomic Snapshots** – Always consistent view; no half-applied updates.
* **Reactive & Dynamic Reload** – Config updates automatically propagate.

### Rule System

* **Unified Fluent API** – Same syntax across all providers.
* **Composable Rules** – `Fetch → Select → Mount → Merge` pipeline.
* **Dynamic Rule Factories** – Generate rules based on earlier snapshots.
* **Advanced Binding** – Classes, interfaces, or factory functions.
* **Partial Recomputation** – Only the earliest changed rule restarts, minimizing churn.
* **Required / Optional rules** – control startup failure vs runtime degradation (`.Required(true/false)`).
* **Conditional execution** – enable rules only when needed (`.When(() => condition)`).

### Providers

* **Static / Observable** – Core built-ins.
* **File** – resilient: falls back to polling when watching fails, then auto-recovers.
* **Environment** – flexible mapping: `__` and `:` delimiters build JSON hierarchy.
* **HTTP** – custom headers, caching, and change-only emissions.
* **Microsoft Adapter Provider** *(extension)* – Bridge existing `IConfiguration`.

### Health Monitoring & Reliability

* **Integrated Health Service** – `IConfigurationHealthService` for all providers.
* [**Health Snapshots**](./docs/health-monitoring.md) – Capture and stream provider health in real time.
* **Metrics Export Hooks** *(experimental)* – Integrate with Prometheus / OpenTelemetry.
* **Cancellation-Aware Reporting** – Accurate health checks under shutdown/load.
* **Error-Resilient Observables** – Streams stay alive even if subscribers throw exceptions.
* **Two-Phase Error Handling** – Fail-fast on init, graceful degradation at runtime.
* **Fail-Safe Defaults** – Config remains valid even on failures.
* **Change Propagation System** – Coalesced updates, consistent application.

### Developer Experience

* **Minimal Boilerplate** – Define a class, add a rule, done.
* **Auto-Registered Reactive Config** – [`IReactiveConfig<T>`](./docs/reactive-config.md) registered automatically in DI.
* **Interface binding** – map POCOs to read-only interfaces (`Bind.Type<T>().To<IMyConfig>()`) for clean contracts.
* **Service Lifetime Control** – Scoped, Singleton, Transient, and Keyed registrations supported.
* **Test-Friendly** – Static/observable providers make testing easy.
* **ASP.NET Core Integration** – Drop-in extensions for DI.
* **Migration Path** – Adapter for Microsoft.Extensions.Configuration.


---

## Install

```bash
dotnet add package Cocoar.Configuration
# For ASP.NET Core integration:
dotnet add package Cocoar.Configuration.AspNetCore
```

---

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register layered configuration (file + environment overlay)
builder.Services.AddCocoarConfiguration([
    Rule.From.File("appsettings.json").Select("App").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
]);

var app = builder.Build();

// Simple endpoint: inject concrete snapshot (Scoped)
app.MapGet("/feature", (AppSettings cfg) => new {
    snapshotFlag = cfg.FeatureFlag,  // Value at scope start (request)
    message = cfg.Message
});
```

## Examples

Each example is a standalone runnable project under `src/Examples/`:

| Project | Description |
|---------|-------------|
| [BasicUsage](src/Examples/BasicUsage) | Common ASP.NET Core pattern (file + env overlay) |
| [FileLayering](src/Examples/FileLayering) | Multiple JSON layering (base + env + local) |
| [DynamicDependencies](src/Examples/DynamicDependencies) | Later rules derive values from earlier configs |
| [AspNetCoreExample](src/Examples/AspNetCoreExample) | Minimal API exposing config via endpoints |
| [GenericProviderAPI](src/Examples/GenericProviderAPI) | Generic provider registration API usage |
| [HttpPollingExample](src/Examples/HttpPollingExample) | Remote HTTP polling configuration pattern |
| [MicrosoftAdapterExample](src/Examples/MicrosoftAdapterExample) | Integrating existing `IConfigurationSource` assets |
| [ServiceLifetimes](src/Examples/ServiceLifetimes) | DI lifetime & keyed registration control |
| [StaticProviderExample](src/Examples/StaticProviderExample) | Static seeding with JSON strings and factory functions |
| [DIExample](src/Examples/DIExample) | Comprehensive DI patterns & overrides |
| [SimplifiedCoreExample](src/Examples/SimplifiedCoreExample) | Pure core (no DI) with `ConfigManager` |
| [BindingExample](src/Examples/BindingExample) | Interface binding without DI |

More details: [Examples README](src/Examples/README.md).

---
## Quality & Reliability

Cocoar.Configuration is backed by an extensive test suite:

* **204 automated tests** across core, providers, and edge cases (v1.0.0).
* Continuous integration (GitHub Actions) validates every PR and commit.
* High coverage of provider behavior, failure handling, and the recompute pipeline.

<details>
<summary><strong>What’s covered beyond unit tests</strong></summary>

* **Integration:** multi‑provider composition, rule ordering, recompute pipeline, snapshot stability.
* **Concurrency & Cancellation:** debounce/coalescing under rapid change storms; overlapping recomputes; cancellation correctness.
* **Stress & Performance:** large/megabyte JSONs; high‑frequency writes; multi‑provider concurrency; emission minimality (fewer emissions than changes).
* **File Provider Resilience:** FileSystemWatcher ⇄ polling fallback; directory deletion/recreation recovery.
* **HTTP Provider “battle tests”:** headers, caching, non‑200 handling, base address vs absolute URIs.
* **Environment & Microsoft Adapter:** delimiter/underscore edge cases; in‑memory config integration.
* **Fuzz/Differential tests:** random change sequences maintain correctness and stable merges.
* **Health pipeline:** status derivation, recovery, version preservation on failure, observable health updates.

</details>

See the [Testing Guide](src/tests/Cocoar.Configuration.Core.Tests/TESTING_GUIDE.md) for patterns and trait filters.

---
## Security Notes

- Do not commit secrets to repo JSON
- Overlay secrets via env/provider layers
- Use TLS + auth for remote polling
- Consider vault integration via Microsoft adapter

---
## Contributing & Versioning

- SemVer (additive MINOR, breaking MAJOR)
- PRs & issues welcome
- Licensed under Apache License 2.0 (explicit patent grant & attribution via NOTICE)

### License & Trademark
This project is licensed under the [Apache License, Version 2.0](LICENSE). See [`NOTICE`](NOTICE) for attribution.

"Cocoar" and related marks are trademarks of COCOAR e.U. Use of the name in forks or derivatives should preserve attribution and avoid implying official endorsement. See [TRADEMARKS](TRADEMARKS.md) for permitted and restricted uses.

---
