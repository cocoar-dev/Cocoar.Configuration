# Cocoar.Configuration

![Cocoar.Configuration](social-preview-small.png)

Lightweight, strongly-typed, deterministic multi-source configuration layering for .NET (current target framework: **net9.0**).

![Build (develop)](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/push-develop.yml/badge.svg)
![PR Validation](https://github.com/cocoar-dev/cocoar.configuration/actions/workflows/pr-develop.yml/badge.svg)
![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)


---

## Why Cocoar.Configuration

Deterministic, strongly-typed, rule-driven configuration layering that complements (not replaces) Microsoft.Extensions.Configuration.

### Design Goals at a Glance

- **Explicit ordered layering**: Deterministic last-write-wins per key.
- **Typed direct injection**: Inject config classes or mapped interfaces (no `IOptions<T>` ceremony).
- **Atomic snapshot recompute**: Full ordered rebuild on change → consistent view for all consumers.
- **Dynamic rule factories**: Later rules can read earlier in-progress snapshots to shape options/queries.
- **Pluggable provider model**: File, environment, HTTP polling, Microsoft adapter, static & custom.
- **DI lifetimes & keys**: Configure singleton (default), scoped, transient, keyed variants per type.
- **Per-type diagnostics**: Inspect merged snapshots when troubleshooting.

### Interoperability

Bring any existing `IConfigurationSource` via the Microsoft Adapter package; adopt incrementally alongside your existing configuration.

### When to Reach for It

Use Cocoar.Configuration when you want reproducible merges, explicit ordering, direct typed DI, dynamic composition between layers, or richer provider extensibility with a unified change model. Keep plain `IConfiguration` when simple hierarchical key/value access with existing providers is sufficient.

---

---

## Packages & Target Framework

Currently only **net9.0** is shipped. Multi-targeting (e.g. `net8.0`) may be added later.

| Package | Description | TFM |
|---------|-------------|-----|
| `Cocoar.Configuration` | Core (file + environment providers, merge orchestration) | net9.0 |
| `Cocoar.Configuration.AspNetCore` | WebApplicationBuilder / DI convenience extensions | net9.0 |
| `Cocoar.Configuration.HttpPolling` | HTTP polling provider | net9.0 |
| `Cocoar.Configuration.MicrosoftAdapter` | Adapter for Microsoft `IConfigurationSource` | net9.0 |

### Install (Example)
```xml
<ItemGroup>
        <PackageReference Include="Cocoar.Configuration" />
        <!-- Optional extensions -->
        <PackageReference Include="Cocoar.Configuration.AspNetCore" />
        <PackageReference Include="Cocoar.Configuration.HttpPolling" />
        <PackageReference Include="Cocoar.Configuration.MicrosoftAdapter" />
</ItemGroup>
```

Quick CLI install commands:
```
dotnet add package Cocoar.Configuration
dotnet add package Cocoar.Configuration.AspNetCore
dotnet add package Cocoar.Configuration.HttpPolling
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```

---

## Quick Start
Minimal example (file + environment layering, strongly-typed access):

```csharp
using Cocoar.Configuration;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

public class AppSettings
{
        public string ConnectionString { get; set; } = "";  // base value in JSON, can be overridden
        public bool EnableFeatureX { get; set; }             // overridden by env var APP_EnableFeatureX
        public int CacheSeconds { get; set; } = 30;          // overridden by env var APP_CacheSeconds
}

var builder = WebApplication.CreateBuilder(args);

builder.AddCocoarConfiguration(
        // Base layer (optional): read a section from a JSON file if it exists
        Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("appsettings.json", "App"))
                .For<AppSettings>().Optional(),

        // Environment variables (prefix APP_) override matching properties
        Rule.From.Environment(_ => new EnvironmentVariableRuleOptions("APP_"))
                .For<AppSettings>()
);

var app = builder.Build();

// Direct typed injection (preferred): resolve your config type directly from DI
var settings = app.Services.GetRequiredService<AppSettings>();
Console.WriteLine($"FeatureX: {settings.EnableFeatureX}, Cache: {settings.CacheSeconds}s");

// Alternative (advanced): you can inject ConfigManager when you need meta access
// var manager = app.Services.GetRequiredService<ConfigManager>();
// var settingsViaManager = manager.GetRequiredConfig<AppSettings>();
```


More examples (multi-project solution under `src/Examples/`):

- **[BasicUsage](src/Examples/BasicUsage/Program.cs)** – File + environment layering pattern (full code)
- **[AspNetCoreExample](src/Examples/AspNetCoreExample/Program.cs)** – Web application integration
- **[FileLayering](src/Examples/FileLayering/Program.cs)** – Multiple JSON layers (deterministic last-write-wins)
- **[ServiceLifetimes](src/Examples/ServiceLifetimes/Program.cs)** – DI lifetimes + keyed registrations
- **[DynamicDependencies](src/Examples/DynamicDependencies/Program.cs)** – Rules reading other config mid-recompute
- **[GenericProviderAPI](src/Examples/GenericProviderAPI/Program.cs)** – Full generic provider control
- **[MicrosoftAdapterExample](src/Examples/MicrosoftAdapterExample/Program.cs)** – Integrate any `IConfigurationSource`
- **[HttpPollingExample](src/Examples/HttpPollingExample/Program.cs)** – Remote polling with change detection
- **[StaticProviderExample](src/Examples/StaticProviderExample/Program.cs)** – Seeding & composition with static rules

Open the solution: [`src/Examples/Examples.sln`](src/Examples/Examples.sln) or run an example directly, e.g.:
```
dotnet run --project src/Examples/BasicUsage
```

> Direct Injection vs `ConfigManager`: In normal application code you inject your typed configuration (class or mapped interface) directly. Inject `ConfigManager` only for meta scenarios (e.g., conditionally accessing multiple config types, diagnostics, dynamic factory logic). All examples now prioritize direct injection; `/manager` in the AspNetCore example shows the alternative.

---

## Concepts

- **Rule**: Defines source + optional query + target configuration type.
- **Provider**: Pluggable source (file, environment, HTTP, adapter, static, custom) that may emit change notifications.
- **Merge**: Ordered last-write-wins per flattened key (`Section:Key`) then rebound to your target type.
- **Arrays**: Replaced as whole values (no element-wise merge) by design.
- **Recompute**: Any emitting provider triggers full ordered recompute → atomic snapshot swap (simple & predictable).
- **Dynamic dependencies**: Rule factories (options/query) can read in-progress snapshots produced earlier in the same recompute.
- **Required vs Optional**: Required rule failure blocks that config type; optional failure skips the layer.
- **DI Lifetimes & Keys**: Register config types as singleton (default), scoped, transient and/or keyed.

### Static Provider

See [`src/Examples/StaticProviderExample/Program.cs`](src/Examples/StaticProviderExample/Program.cs) for seeding defaults and composing dependent configuration.

---

## Advanced Features

### Service Lifetimes & Keyed Services
Control how configuration types are registered in DI container. Default is Singleton, but you can specify Scoped/Transient and use keyed services for multiple configurations of the same type.
→ Example: [`src/Examples/ServiceLifetimes/Program.cs`](src/Examples/ServiceLifetimes/Program.cs)

### Generic Provider API  
Use `Rule.From.Provider<TProvider, TOptions, TQuery>()` for full control over any provider type, including third-party providers.
→ Example: [`src/Examples/GenericProviderAPI/Program.cs`](src/Examples/GenericProviderAPI/Program.cs)

### Microsoft Configuration Adapter
Plug any Microsoft `IConfigurationSource` (JSON, XML, Key Vault, User Secrets, etc.) into Cocoar's rule-based system.
→ Example: [`src/Examples/MicrosoftAdapterExample/Program.cs`](src/Examples/MicrosoftAdapterExample/Program.cs)

### HTTP Polling Provider
Fetch configuration from HTTP endpoints with automatic polling. Only triggers recomputes when response actually changes.
→ Example: [`src/Examples/HttpPollingExample/Program.cs`](src/Examples/HttpPollingExample/Program.cs)

## Providers (Built-in & Extensions)

| Provider | Package | Change Signal | Notes |
|----------|---------|---------------|-------|
| **File (JSON)** | Core | ✅ Filesystem watcher (debounced) | Paths/sections; good base layer |
| **Environment** | Core | ❌ Snapshot only | Prefix filter; `__` & `:` nesting |
| **HTTP Polling** | Extension | ✅ On real payload change | Optional headers; polling interval |
| **Microsoft Adapter** | Extension | Depends on source | Wrap any `IConfigurationSource` |

**All providers support:** Optional/required rules, dynamic factories, provider instance pooling.

**Provider Documentation:**
- [File Provider](src/Cocoar.Configuration/Providers/FileSourceProvider/README.md) - JSON files with filesystem watching
- [Environment Provider](src/Cocoar.Configuration/Providers/EnvironmentVariableProvider/README.md) - Environment variables with prefix filtering  
- [HTTP Polling Provider](src/Cocoar.Configuration.HttpPolling/README.md) - HTTP endpoint polling _(separate package)_
- [Microsoft Adapter](src/Cocoar.Configuration.MicrosoftAdapter/README.md) - IConfigurationSource integration _(separate package)_


Semantics: Use Optional for non-critical layers; Required enforces presence.

### Extensibility

Create third‑party providers by implementing the generic provider base and adding fluent entry points (e.g. `Rule.From.MyProvider()`). See the [Provider Development Guide](src/Cocoar.Configuration/Providers/README.md) for:
- Custom provider implementation patterns
- Fluent API extension guidance
- Instance lifecycle & change emission
- Testing strategies

Users just install the provider package and compose alongside built-ins.

---

---

## Security

* **Never commit secrets** to JSON files in your repository  
* Use **environment variable overlays** or dedicated secret management systems  
* For remote providers: Always use **TLS**, set reasonable **timeouts**, and include **auth headers** when needed  
* Consider using Azure Key Vault, AWS Secrets Manager, or similar via the **Microsoft Adapter**

---

## Examples

Multi-project examples live under [`src/Examples/`](src/Examples/) (see [`src/Examples/README.md`](src/Examples/README.md)). Each folder is a runnable console (or minimal) project with its own `Program.cs`:

### Core Examples
- `BasicUsage` – File + environment layering
- `AspNetCoreExample` – Web app integration
- `FileLayering` – Multi-file deterministic layering

### Advanced Configuration
- `ServiceLifetimes` – DI lifetimes & keyed registrations
- `DynamicDependencies` – Reading prior config during recompute

### Provider Extensions
- `GenericProviderAPI` – Generic provider composition
- `MicrosoftAdapterExample` – Adapting `IConfigurationSource`
- `HttpPollingExample` – Remote polling with change detection
- `StaticProviderExample` – Static seeding & dependent composition

Run any example:
```
dotnet run --project src/Examples/GenericProviderAPI
```

> **Quality Assurance**: The examples solution ([`src/Examples/Examples.sln`](src/Examples/Examples.sln)) is built in CI to ensure examples stay aligned with the API. Functional behaviors are additionally covered by unit/integration tests.

---

## Thread Safety & Performance

- **Thread-safety**: Reading configuration is thread-safe. Recompute produces a new snapshot and swaps atomically.
- **Recompute cost**: Full merge of all rules (O(n) w.r.t. rule count + JSON size). Partial recompute is on the roadmap.
- **Provider reuse**: Instances reused while instance options remain stable; query changes rebuild subscriptions.

---

## Deep Dive: Execution & Merge Pipeline

- Each rule targets one configuration type and queries exactly one provider to contribute a structured layer.
- A recompute builds an ordered list of layers, flattens them into colon keys, applies last-write-wins per key, then materializes a fresh typed snapshot.
- Dynamic factories may read snapshots already built earlier in the recompute to influence later rule options or queries.
- Arrays are replaced whole; objects merge by key; nulls follow default JSON deserialization semantics.

### Change Model

- Providers may emit change notifications (e.g., file watcher, HTTP polling). The environment provider typically does not emit by default and is treated as snapshot input.
- On any provider change, Cocoar recomputes all rules for all target types in order and atomically swaps the cache. Consumers see consistent snapshots.
- If your rule factories (options/query) depend on current config, provider instances/subscriptions are rebuilt during recompute so dynamic dependencies take effect.

### Required vs Optional Rules

- Each rule can be marked required or optional.
- Required: failures (e.g., missing file, HTTP error) cause the recompute to fail for that rule/type.
- Optional: failures are tolerated and the rule is skipped for that recompute.

### Ordering & Dependencies

- Place dependency-producing rules before dependency-consuming rules.
- Rules may read any type's current snapshot during recompute. Avoid circular dependencies across types or rules to prevent surprises.

**Guidance for recompute-time reads:**
- GetRequiredConfig<T>() throws if T does not exist yet; use only if you guarantee T is produced earlier.
- GetConfig<T>() returns null if T does not exist; handle nulls explicitly when reading dependencies.
- For guaranteed existence, seed the dependency type with an explicit rule (e.g., a static provider/factory rule — see `Rule.From.Static`).

### Merge Semantics & Limits

- Last-write-wins, key-by-key merge of JSON objects using colon-key flattening.
- Arrays are replace-only by design; an array value replaces the prior value at that key (no merging).
- Keys follow `Section:Key` flattening during merge; final objects are unflattened before binding to your types.

---

## Versioning & Stability

- Stable releases follow **SemVer**; see GitHub Releases or NuGet version history for changes.
- Breaking changes only in MAJOR versions; MINOR for additive features; PATCH for fixes.
- Provider abstractions evolve conservatively.

> Packages are published under the NuGet organization **cocoar**.

---

## Contributing

Issues and PRs welcome. Please keep provider abstractions stable and deterministic (e.g., option keys for instance pooling) and follow the merge semantics described in ARCHITECTURE.md.

**📝 Documentation Quality**: Projects under [`src/Examples/`](src/Examples/) are built in CI to ensure they remain in sync with the current API. Keep examples minimal and idiomatic when contributing.

---

For deeper details, examples, and roadmap, check src/Cocoar.Configuration/README.md and ARCHITECTURE.md.


## Roadmap (Short)

- **Partial recompute:** Only recompute rules downstream from changed providers to reduce work on frequent changes
- **Provider pooling:** Better lifecycle reuse across recomputes with `IDisposable` support for long-lived connections  
- **Clean up naming:** Minor inconsistencies (e.g., env prefix vs memberPath terminology) for better API consistency
- **Additional providers:** HTTP Server-Sent Events, SignalR live streams, timer-less push models
- **Nullability improvements:** Tidy up nullable reference type annotations across the API surface

See [ARCHITECTURE.md](src/Cocoar.Configuration/ARCHITECTURE.md) & provider READMEs for details.

---

*(This README reflects the current code state – future multi-targeting or optimizations will be documented when implemented.)*

---

<!-- Comparison moved near top as "Why Cocoar Instead of Plain IConfiguration?" -->