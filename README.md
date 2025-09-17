# Cocoar.Configuration

> Powerful layered configuration for .NET  
> Simple • Strongly typed • Reactive

![Cocoar.Configuration](social-preview-small.png)

![Build (develop)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml/badge.svg)
![PR Validation](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml/badge.svg)
![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)

---
## Why?

Most apps just need: *"Give me my configs, layered, strongly typed, and keep them fresh."*

Cocoar.Configuration lets you:
- Define a few ordered **rules** → get ready-to-inject **types**
- Layer **file + env + http + static** sources deterministically (last-write-wins per key)
- Get **push updates** automatically via `IReactiveConfig<T>` (no extra setup)

If you need more later (interface binding, DI lifetime control, custom providers) it’s all there—opt‑in, not in your face.

---
## Quick Start (Minimal ASP.NET Core)

`appsettings.json`:
```json
{
  "App": {
    "FeatureFlag": true,
    "Message": "Hello from config"
  }
}
```

`Program.cs`:
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

// Reactive usage example (background logging)
var reactive = app.Services.GetRequiredService<IReactiveConfig<AppSettings>>();
var _ = reactive.Subscribe(c => Console.WriteLine($"[Config Updated] FeatureFlag={c.FeatureFlag}"));

app.Run();

public sealed class AppSettings
{
    public bool FeatureFlag { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

`appsettings.json` changed? — future requests see the updated snapshot; reactive stream subscribers get a push..

---
## Reactive by Default

Every configuration type you map gets a free reactive companion:

| You Have | You Also Automatically Have |
|----------|------------------------------|
| `AppSettings` | `IReactiveConfig<AppSettings>` |

`IReactiveConfig<T>` gives you:
- `CurrentValue` – latest stable value
- `Subscribe(...)` – push updates only when actual content changes
- Safe & error-resilient stream: subscriber exceptions are logged, never terminate the pipeline

**Guidance:**
- In request/short-lived scopes: inject the concrete type (`AppSettings`) for a consistent snapshot
- In background services / singletons: inject `IReactiveConfig<T>`
- Need a frozen value inside a singleton? Capture `live.CurrentValue` once (rarely required)

---
## Rule Basics

A **rule** = provider + (optional selection/query) + target config type.
Order matters: later rules overwrite earlier values per flattened key (deterministic last-write-wins).

```csharp
var rules = new [] {
    Rule.From.File("appsettings.json").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
};
```

Additional options (add only when needed):
```csharp
Rule.From.File("secrets.json")
    .Select("Secrets:Db")        // select subtree
    .Mount("Database")           // mount under a new path
    .Required()                  // fail the rule if source missing
    .For<DatabaseConfig>();
```

---
## Interface Binding (Optional)

Start without it. Add when you want to inject contracts instead of concretes.
```csharp
services.AddCocoarConfiguration(rules, [
    Bind.Type<AppSettings>().To<IAppSettings>()
]);

public sealed class Handler(IAppSettings cfg, IReactiveConfig<IAppSettings> live)
{ /* ... */ }
```
More patterns: [BINDING.md](docs/BINDING.md).

---
## DI Lifetimes & Defaults

By default:
- Concrete config types: **Scoped** (one snapshot per request/scope)
- `IReactiveConfig<T>`: **Singleton** (continuous live updates)

Change the default lifetime:
```csharp
services.AddCocoarConfiguration(rules, configureServices: o =>
    o.DefaultRegistrationLifetime(ServiceLifetime.Singleton));
```
Disable auto reactive registration (rare):
```csharp
o.DisableAutoReactiveRegistration();
```
Manual overrides & keyed registrations: see [ADVANCED.md](docs/ADVANCED.md).

**Choosing a Lifetime:**
| Scenario | Lifetime |
|----------|----------|
| Typical web request consumption | Scoped |
| High-read immutable small config | Singleton |
| Background service (reactive) | Scoped + `IReactiveConfig<T>` (preferred) |
| Large object, avoid mid-request drift | Scoped |

---
## Providers (Built-In & Extensions)

| Provider          | Package   | Change Signal        | Notes                             |
| ----------------- | --------- | -------------------- | --------------------------------- |
| Static            | Core      | ❌                    | Seed defaults, compose values     |
| File (JSON)       | Core      | ✅ FS watcher         | Deterministic layering            |
| Environment       | Core      | ❌                    | Prefix filter; `__` / `:` nesting |
| HTTP Polling      | Extension | ✅ Interval polling   | Payload diffing (streaming hash)  |
| Microsoft Adapter | Extension | Depends              | Any `IConfigurationSource`        |

Detailed provider docs: [PROVIDERS.md](docs/PROVIDERS.md).

---
## Performance & Reliability (Short Version)

- Atomic recompute + full snapshot publish
- Incremental: recompute only from earliest changed rule
- Streaming JSON → MD5 hashing (no intermediate string allocations)
- Hash-gated reactive emissions (no duplicate pushes)
- Error-resilient reactive pipelines (no dead observables)
- 80+ tests (stress, cancellation, differential correctness)

Deep dive: [ARCHITECTURE.md](docs/ARCHITECTURE.md), [CONCEPTS.md](docs/CONCEPTS.md).

---
## When You Need More

| Need | Go To |
|------|-------|
| Interface patterns | [docs/BINDING.md](docs/BINDING.md) |
| Advanced DI control | [docs/ADVANCED.md](docs/ADVANCED.md) |
| Architecture & pipeline | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Providers overview | [docs/PROVIDERS.md](docs/PROVIDERS.md) |
| Migration notes | [docs/MIGRATION.md](docs/MIGRATION.md) |
| Build your own provider | [docs/PROVIDER_DEV.md](docs/PROVIDER_DEV.md) |
| Deep scenarios (lifecycle, dynamic factories, tuning) | [docs/DEEP_DIVE.md](docs/DEEP_DIVE.md) |

---
## Installation

```xml
<ItemGroup>
  <PackageReference Include="Cocoar.Configuration" />
  <PackageReference Include="Cocoar.Configuration.DI" />
  <!-- Optional -->
  <PackageReference Include="Cocoar.Configuration.HttpPolling" />
  <PackageReference Include="Cocoar.Configuration.MicrosoftAdapter" />
  <PackageReference Include="Cocoar.Configuration.AspNetCore" />
</ItemGroup>
```

CLI:
```bash
dotnet add package Cocoar.Configuration
dotnet add package Cocoar.Configuration.DI
```
(Add extensions only when needed.)

---
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
| [StaticProviderExample](src/Examples/StaticProviderExample) | Static seeding + dependent recompute |
| [DIExample](src/Examples/DIExample) | Comprehensive DI patterns & overrides |
| [SimplifiedCoreExample](src/Examples/SimplifiedCoreExample) | Pure core (no DI) with `ConfigManager` |
| [BindingExample](src/Examples/BindingExample) | Interface binding without DI |

More details: [Examples README](src/Examples/README.md).

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
