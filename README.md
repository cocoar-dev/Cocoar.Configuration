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

Deterministic, strongly-typed, rule-driven configuration layering that **complements** `Microsoft.Extensions.Configuration`.

### Design Goals at a Glance

- **Explicit ordered layering**: Deterministic last-write-wins per key.
- **Typed direct injection**: Inject config classes or mapped interfaces (no `IOptions<T>` ceremony).
- **Atomic snapshot recompute**: Full ordered rebuild on change → consistent view for all consumers.
- **Dynamic rule factories**: Later rules can read earlier in-progress snapshots to shape options/queries.
- **Pluggable provider model**: File, environment, HTTP polling, Microsoft adapter, static & custom.
- **DI lifetimes & keys**: Configure singleton (default), scoped, transient, keyed variants per type.
- **Per-type diagnostics**: Inspect merged snapshots when troubleshooting.
- **Interoperability**: Bring any existing `IConfigurationSource` via the Microsoft Adapter package.

---

## Installation

Supported TFM: **net9.0** (multi-targeting planned).

```xml
<ItemGroup>
        <PackageReference Include="Cocoar.Configuration" />
        <!-- Optional extensions -->
        <PackageReference Include="Cocoar.Configuration.DI" />
        <PackageReference Include="Cocoar.Configuration.AspNetCore" />
        <PackageReference Include="Cocoar.Configuration.HttpPolling" />
        <PackageReference Include="Cocoar.Configuration.MicrosoftAdapter" />
</ItemGroup>
```

CLI:

```sh
dotnet add package Cocoar.Configuration
dotnet add package Cocoar.Configuration.DI
dotnet add package Cocoar.Configuration.AspNetCore
dotnet add package Cocoar.Configuration.HttpPolling
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```

---

## Quick Start

### Simple Setup (Auto-Registration)
Add rules; everything (concrete types + manager) is available immediately.
```csharp
services.AddCocoarConfiguration([rules]);
```
➡ Full runnable: `Examples/BasicUsage`

### Interface Binding Setup
Add interface mappings (optional, works with or without DI):
```csharp
services.AddCocoarConfiguration([rules], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
]);
```
➡ Full runnable: `Examples/BindingExample`

### Advanced Setup (Full Control)
Override lifetimes, disable or extend auto-registration, add keyed services:
```csharp
services.AddCocoarConfiguration([rules], [bindings], opts => {
    opts.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
    opts.Register.Add<IPaymentConfig>(ServiceLifetime.Scoped, "backup");
});
```
➡ Full runnable: `Examples/ServiceLifetimes`

### Binding vs DI Registration

These two concerns are independent and intentionally separated:

| Concern | What You Define | Purpose | Where |
|---------|------------------|---------|-------|
| Binding | `Bind.Type<PaymentConfig>().To<IPaymentConfig>()` | Map concrete config types to one or more interfaces for clean consumption | Core (works without DI) |
| DI Registration | `options.Register.Add<IPaymentConfig>(ServiceLifetime.Scoped, "backup")` | Control lifetimes, add/remove, keyed registrations in the host container | DI Package (`Cocoar.Configuration.DI`) |

Key points:
- You can use bindings without DI (access via `ConfigManager` interface lookups).
- You can use DI auto-registration without any bindings (inject concrete types directly).
- Combine them for the richest experience: bind interfaces, let DI auto-register both.
- Disable auto-registration with `options.DefaultRegistrationLifetime(null)` and take full manual control via `options.Register`.

Minimal patterns:
```csharp
services.AddCocoarConfiguration([rules]);                 // Concrete only
services.AddCocoarConfiguration([rules], [bindings]);      // + Interfaces
services.AddCocoarConfiguration([rules], [bindings], opts => { /* control */ });
```

#### When to Add Bindings?
- Start with concrete types only while exploring providers.
- Add a binding when: multiple consumers need a narrowed contract, or you want to hide writeable/internal members.
- Bind multiple interfaces to the same concrete when different views (read-only, subset) are needed.
- Skip bindings entirely if you only inject config into a composition root or a small number of services.

## Concepts

* **Rule**: Source + optional query + target configuration type
* **Binding**: Maps concrete configuration types to interfaces for clean DI
* **Provider**: Pluggable source (file, env, HTTP, static, custom, adapter)
* **Merge**: Ordered *last-write-wins* per flattened key
* **Recompute**: Incremental – only recompute from earliest changed rule; atomic snapshot publish.
* **Dynamic dependencies**: Rule factories (options/query) can read earlier in-progress rule outputs during a pass.
* **Required vs Optional**: Optional failure skips the layer.
* **DI Auto-Registration**: Auto-registers config types and bound interfaces - configurable and can be disabled.
* **Service Control**: Fine-grained Add/Remove control over service lifetimes and keys

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

* **Complete DI Integration**: Zero-config auto-registration with `Cocoar.Configuration.DI` package
* **Interface Binding System**: Clean separation with `Bind.Type<T>().To<Interface>()` mappings
* **Service Lifetime Control**: Configurable default lifetimes plus fine-grained Remove/Add methods  
* **Keyed Services**: Multiple registrations per type with service keys
* **Fail-Safe API**: Impossible to forget method calls - always works out of the box
* **Generic Provider API**: `Rule.From.Provider<>()` for full control
* **Microsoft Adapter**: wrap any `IConfigurationSource`
* **HTTP Polling Provider**: auto-change detection

👉 Details in [Advanced Features](docs/ADVANCED.md)

---

## Security

* **Never commit secrets** to JSON files in your repository  
* Use **environment variable overlays** or dedicated secret management systems  
* For remote providers: Always use **TLS**, set reasonable **timeouts**, and include **auth headers** when needed  
* Consider using Azure Key Vault, AWS Secrets Manager, or similar via the **Microsoft Adapter**

---

## Examples

Multi-project solution under [`src/Examples/`](src/Examples/) with runnable demos:

**Core Examples:**
- **[BasicUsage](src/Examples/BasicUsage/Program.cs)** – File + environment layering with interface binding
- **[SimplifiedCoreExample](src/Examples/SimplifiedCoreExample/Program.cs)** – Pure core library usage (no DI)
- **[BindingExample](src/Examples/BindingExample/Program.cs)** – Interface binding without DI frameworks

**DI Integration:**  
- **[DIExample](src/Examples/DIExample/Program.cs)** – Comprehensive DI integration showcase with advanced patterns
- **[ServiceLifetimes](src/Examples/ServiceLifetimes/Program.cs)** – Service lifetimes + keyed registrations with new API
- **[AspNetCoreExample](src/Examples/AspNetCoreExample/Program.cs)** – Web application integration

**Advanced Patterns:**
- **[FileLayering](src/Examples/FileLayering/Program.cs)** – Multiple JSON layers (deterministic last-write-wins)
- **[DynamicDependencies](src/Examples/DynamicDependencies/Program.cs)** – Rules reading other config mid-recompute
- **[GenericProviderAPI](src/Examples/GenericProviderAPI/Program.cs)** – Full generic provider control
- **[StaticProviderExample](src/Examples/StaticProviderExample/Program.cs)** – Seeding & composition with static rules

**Provider Extensions:**
- **[MicrosoftAdapterExample](src/Examples/MicrosoftAdapterExample/Program.cs)** – Integrate any `IConfigurationSource`
- **[HttpPollingExample](src/Examples/HttpPollingExample/Program.cs)** – Remote polling with change detection

---

## Deep Dive Documentation

For more in-depth documentation, see:

* [Migration Guide](docs/MIGRATION.md) – migrate from legacy fluent `.As<T>()` API to Binding + DI options
* [Architecture](docs/ARCHITECTURE.md) – execution & merge pipeline, binding system, DI integration architecture
* [Binding System](docs/BINDING.md) – interface mapping, resolution, guidelines
* [Advanced Features](docs/ADVANCED.md) – complete DI integration, service lifetime control, interface binding patterns
* [Concepts](docs/CONCEPTS.md) – rules, merge semantics, binding system, auto-registration concepts  
* [Providers](docs/PROVIDERS.md) – static, file, env, HTTP, Microsoft adapter
* [Examples](src/Examples/README.md) – runnable samples
* [Provider Development Guide](docs/PROVIDER_DEV.md) – build your own provider

---

## Thread Safety & Performance

* Reading config is thread-safe (atomic snapshot swap)
* Incremental recompute: only from earliest changed rule onward (prefix reused)
* Selection-hash gating: unchanged selected subtree events skipped
* Providers reused across recomputes when instance options stable
* Static rule set: rules immutable after initialization (use `UseWhen` to toggle)

---

## Quality & Reliability

This project invests heavily in **correctness-first incremental recompute**. Optimisations (prefix reuse, cancellation, selection‑hash gating, debounce) are all guarded by strong differential and stress tests so performance never compromises determinism.

Core test suites (see `src/tests/`):

| Suite | Focus | Guarantee
|-------|-------|----------|
| `DifferentialCorrectnessFuzzTests` | Random multi-provider mutation waves | Final published snapshot bit-for-bit equals a naive full merge |
| `PartialRecomputeTests` | Prefix reuse / earliest-index accuracy | Unchanged prefix providers are never refetched |
| `OverlappingRecomputeCorrectnessTests` | Cancellation under descending storms | No lost updates; latest versions survive heavy overlap |
| `CancellationTests` | Mid-pass abort & restart | Earlier changes preempt wasted later work |
| `SnapshotChangeDeletionTests` | Deletion propagation | Removed keys do not resurrect spuriously |
| `RecomputeStressTests` | Burst & jitter durability | Bounded passes; stable end-state |
| Provider suites (`Providers/*Tests`) | Integration of file/env/http/adapter | Source-specific semantics remain correct |

**Why call this out?** Incremental configuration layering is deceptively complex once you introduce cancellation and reuse. Many libraries silently drop updates or leak stale keys; these suites explicitly prevent that class of regression.


---

## Versioning & Stability

- Stable releases follow **SemVer**; see GitHub Releases or NuGet version history for changes.
- Breaking changes only in MAJOR versions; MINOR for additive features; PATCH for fixes.
- Provider abstractions evolve conservatively.

> Packages are published under the NuGet organization **cocoar**.

## Contributing

Issues and PRs are welcome 🎉
Keep provider abstractions stable & deterministic.
Examples and docs are validated in CI.

---

*(This README reflects the current state – future optimizations & multi-targeting will be documented in `docs/`.)*
