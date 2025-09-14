# Cocoar.Configuration

![Cocoar.Configuration](social-preview-small.png)

Lightweight, strongly-typed, deterministic multi-source configuration layering for .NET
(Current target framework: **net9.0**).

![Build (develop)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml/badge.svg)
![PR Validation](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml/badge.svg)
![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)

---

## Why Cocoar.Configuration?

Deterministic, strongly-typed, rule-driven configuration layering that **complements (not replaces)** `Microsoft.Extensions.Configuration`.

**Design Goals**

* Explicit ordered layering → deterministic *last-write-wins*
* Strongly-typed direct injection (no `IOptions<T>`)
* Atomic recompute snapshots → consistent view for all consumers
* Dynamic rule factories (rules can depend on earlier snapshots)
* Pluggable provider model (file, env, HTTP, Microsoft adapter, static, custom)
* Flexible DI lifetimes & keys
* Per-type diagnostics

**When to use it**

* You need reproducible merges with explicit ordering
* You want direct typed DI or dynamic composition between layers
* You need richer provider extensibility with a unified change model

Stick with plain `IConfiguration` if hierarchical key/value access is enough.

---

## Installation

Supported TFM: **net9.0** (multi-targeting planned).

```xml
<ItemGroup>
        <PackageReference Include="Cocoar.Configuration" />
        <!-- Optional extensions -->
        <PackageReference Include="Cocoar.Configuration.AspNetCore" />
        <PackageReference Include="Cocoar.Configuration.HttpPolling" />
        <PackageReference Include="Cocoar.Configuration.MicrosoftAdapter" />
</ItemGroup>
```

CLI:

```sh
dotnet add package Cocoar.Configuration
dotnet add package Cocoar.Configuration.AspNetCore
dotnet add package Cocoar.Configuration.HttpPolling
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```

---

## Quick Start

Minimal example (file + environment layering, strongly-typed access):

```csharp
// ...
builder.AddCocoarConfiguration(
                Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("appsettings.json", "App"))
                                .For<AppSettings>().Optional(),

                Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("APP_"))
                                .For<AppSettings>()
);
```

Then inject your config type directly:

```csharp
var settings = app.Services.GetRequiredService<AppSettings>();
Console.WriteLine($"FeatureX: {settings.EnableFeatureX}");
```

More examples in [`src/Examples/`](src/Examples/) or run:

```sh
dotnet run --project src/Examples/BasicUsage
```

---

## Concepts

* **Rule**: Source + optional query + target configuration type
* **Provider**: Pluggable source (file, env, HTTP, static, custom, adapter)
* **Merge**: Ordered *last-write-wins* per flattened key
* **Recompute**: Any change → full recompute → atomic snapshot swap
* **DI Lifetimes & Keys**: Register as singleton (default), scoped, transient, keyed

👉 [Read more in the **Concepts Deep Dive**](docs/CONCEPTS.md)

---

## Providers

Built-in and extension providers:

| Provider          | Package   | Change Signal        | Notes                             |
| ----------------- | --------- | -------------------- | --------------------------------- |
| Static            | Core      | ❌                    | Seed defaults, compose values     |
| File (JSON)       | Core      | ✅ Filesystem watcher | Deterministic layering            |
| Environment       | Core      | ❌                    | Prefix filter; `__` & `:` nesting |
| HTTP Polling      | Extension | ✅                    | Interval polling, payload diffing |
| Microsoft Adapter | Extension | Depends              | Any `IConfigurationSource`        |

👉 See [Providers Overview](docs/PROVIDERS.md) for full details.

---

## Advanced Features

* **Service Lifetimes & Keys**: control DI lifetimes, keyed configs
* **Generic Provider API**: `Rule.From.Provider<>()` for full control
* **Microsoft Adapter**: wrap any `IConfigurationSource`
* **HTTP Polling Provider**: auto-change detection

👉 Details in [Advanced Features](docs/ADVANCED.md)

---

## Security

* Never commit secrets to repo
* Prefer environment variable overlays / secret managers
* For remote providers: use TLS, timeouts, auth headers
* Key Vault / Secrets Manager integration via Microsoft Adapter

---

## Examples

Multi-project solution under [`src/Examples/`](src/Examples/) with runnable demos:

* BasicUsage – File + env layering
* AspNetCoreExample – Web integration
* FileLayering – Deterministic multi-file layering
* ServiceLifetimes – DI lifetimes + keyed registrations
* DynamicDependencies – rules reading other config
* GenericProviderAPI – provider extensibility
* MicrosoftAdapterExample – IConfigurationSource integration
* HttpPollingExample – remote polling
* StaticProviderExample – seeding & dependent composition

---

## Deep Dive Documentation

For more in-depth documentation, see:

* [Architecture](docs/ARCHITECTURE.md) – execution & merge pipeline, change model
* [Providers](docs/PROVIDERS.md) – static, file, env, HTTP, Microsoft adapter
* [Concepts](docs/CONCEPTS.md) – rules, merge semantics, dependencies
* [Examples](src/Examples/README.md) – runnable samples
* [Provider Development Guide](docs/PROVIDER_DEV.md) – build your own provider

---

## Thread Safety & Performance

* Reading config is thread-safe
* Recompute is O(n) per rules + JSON size
* Providers reused across recomputes when options stable

---

## Versioning & Roadmap

* Follows **SemVer**
* Breaking changes → MAJOR, new features → MINOR, fixes → PATCH
* Roadmap highlights:

        * Partial recompute
        * Provider pooling / IDisposable support
        * Additional providers (SSE, SignalR push)
        * Nullability improvements

---

## Contributing

Issues and PRs are welcome 🎉
Keep provider abstractions stable & deterministic.
Examples and docs are validated in CI.

---

*(This README reflects the current state – future optimizations & multi-targeting will be documented in `docs/`.)*
