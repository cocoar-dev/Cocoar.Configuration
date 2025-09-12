# Cocoar.Configuration

Lightweight, strongly-typed configuration aggregation for .NET. Compose config from multiple sources (files, environment, HTTP, Microsoft IConfiguration) with predictable la## Service Lifetimes and Dependency Injection

Configuration types can be registered with different service lifetimes to control when instances are created and how long they live in your dependency injection container.

### Available Lifetimes

- **Singleton** (default): One instance for the entire application lifetime
- **Scoped**: One instance per scope (e.g., per HTTP request in ASP.NET Core)
- **Transient**: New instance every time it's requested

### Basic Usage

```csharp
// Singleton (default behavior - backward compatible)
Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./config.json"))
    .For<MySettings>()
    .AsSingleton<IMySettings>()  // Explicit singleton
    .Build();

// Scoped - new instance per scope
Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./config.json"))
    .For<MySettings>()
    .AsScoped<IMySettings>()
    .Build();

// Transient - new instance every time
Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./config.json"))
    .For<MySettings>()
    .AsTransient<IMySettings>()
    .Build();
```

### Multiple Registrations with Keys

You can register the same configuration type with different lifetimes using service keys:

```csharp
var rules = Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./config.json"))
    .For<MySettings>()
    .AsSingleton<IMySettings>("cache")      // Singleton for caching
    .AsScoped<IMySettings>("request")       // Scoped for request processing  
    .AsTransient<IMySettings>("temp")       // Transient for temporary use
    .BuildRules();  // Returns multiple rules

builder.Services.AddCocoarConfiguration(rules);

// Resolve with keys
var cached = serviceProvider.GetRequiredKeyedService<IMySettings>("cache");
var requestScoped = serviceProvider.GetRequiredKeyedService<IMySettings>("request");
var temp = serviceProvider.GetRequiredKeyedService<IMySettings>("temp");
```

### Rules and Limitations

- Each lifetime can be registered **once without a key** per rule
- Each lifetime can be registered **multiple times with different keys**
- Cannot register the same lifetime with the same key twice
- Keys must be unique within the same lifetime

```csharp
// ✅ Valid: Different lifetimes
Rule.From.File(...)
    .For<MySettings>()
    .AsSingleton<IMySettings>()     // No key
    .AsScoped<IMySettings>()        // No key
    .AsTransient<IMySettings>();    // No key

// ✅ Valid: Same lifetime, different keys  
Rule.From.File(...)
    .For<MySettings>()
    .AsSingleton<IMySettings>("key1")
    .AsSingleton<IMySettings>("key2");

// ❌ Invalid: Same lifetime and key
Rule.From.File(...)
    .For<MySettings>()
    .AsSingleton<IMySettings>("same-key")
    .AsSingleton<IMySettings>("same-key");  // Throws InvalidOperationException
```

### Backward Compatibility

All existing code continues to work unchanged. Rules without explicit lifetime registration automatically default to singleton behavior:

```csharp
// This still works exactly as before
var rule = Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./config.json"))
    .For<MySettings>()
    .Build();  // Implicitly creates singleton registration
```

## Known gaps and next steps

- Array merge semantics: Arrays currently replace prior values. Additional strategies (append/merge/custom) are under consideration.
- Null/empty handling: Edge cases (nulls and empty objects) will be documented precisely; current behavior follows JSON deserialization defaults after key merge.
- Change emissions: Environment provider does not emit changes by default (snapshot only). If you need change-driven recompute, combine with other providers.
- Circular dependencies: Rules can read any type's current snapshot during recompute. Avoid cycles; detection/guardrails may be added.e-wins merge and live recompute on changes.

## Why

- Keep your app’s config as contracts (interfaces/classes) instead of string lookups
- Layer multiple sources in a fixed order with simple, deterministic merges
- React to changes (file watchers, polling, etc.) and atomically swap results
- Use or build providers without coupling them to ASP.NET Core

## Packages

- Core: `Cocoar.Configuration` (rules, manager, built-in file/env providers)
- ASP.NET Core extras: `Cocoar.Configuration.AspNetCore` (builder integration)
- Providers:
  - File JSON: in core under `Providers/FileSourceProvider`
  - Environment variables: in core under `Providers/EnvironmentVariableProvider`
  - HTTP polling: `Cocoar.Configuration.HttpPolling` (separate package)
  - Microsoft IConfiguration adapter: `Cocoar.Configuration.MicrosoftAdapter` (separate package)

Provider docs:
- File: src/Cocoar.Configuration/Providers/FileSourceProvider/README.md
- Environment: src/Cocoar.Configuration/Providers/EnvironmentVariableProvider/README.md
- HTTP polling: src/Cocoar.Configuration.HttpPolling/README.md
- Microsoft adapter: src/Cocoar.Configuration.MicrosoftAdapter/README.md

Architecture details: src/Cocoar.Configuration/ARCHITECTURE.md

> **📋 Validated Examples**: All code examples in this README are covered by automated tests in [`ReadmeExamplesTests.cs`](src/tests/Cocoar.Configuration.Tests/ReadmeExamplesTests.cs). This ensures they stay accurate and functional. You can also view these examples in your IDE where intellisense and error highlighting will help you understand the API.

## Install

Add the packages you need from NuGet:
- Cocoar.Configuration
- Cocoar.Configuration.AspNetCore (optional)
- Cocoar.Configuration.HttpPolling (optional)
- Cocoar.Configuration.MicrosoftAdapter (optional)

## Quick start

1) Define your settings

```csharp
public interface IMySettings 
{ 
    bool Enabled { get; } 
    int Value { get; } 
}

public sealed class MySettings : IMySettings 
{ 
    public bool Enabled { get; set; } 
    public int Value { get; set; } 
}
```

2) Build rules and the manager

```csharp
using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent;

var rules = new []
{
    Rules.Using.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", "MySection"))
        .For<MySettings>()
        .As<IMySettings>()
        .Build(),
    Rules.Using.FromEnvironment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: "MYAPP_"))
        .For<MySettings>()
        .As<IMySettings>()
        .Build()
};

var manager = new ConfigManager(rules).Initialize();
var cfg = manager.GetConfig<IMySettings>();
```

3) ASP.NET Core builder integration (optional)

```csharp
using Cocoar.Configuration.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCocoarConfiguration(rules);

var app = builder.Build();
var cfg = app.Services.GetRequiredService<ConfigManager>().GetConfig<IMySettings>();
```

## How it works

- You define rules. Each rule targets a specific config type (class/interface) and queries exactly one provider to produce a JSON object.
- For a given type T, Cocoar starts from T’s defaults and merges each rule’s JSON into T in the configured order (last-write-wins, key-by-key).
- During a recompute, a rule can read the current in-progress snapshot from the ConfigManager (any type). This enables dynamic rules whose options depend on values produced by earlier rules in the same recompute.
    - Example: One rule sets a URL; a later HTTP rule reads that URL via `configManager.GetRequiredConfig<MyHttpPollingSettings>()` and uses it for its request.
- Objects are flattened into colon-keys (e.g., `Section:Enabled`), merged in order, then unflattened and deserialized into your target type.

### Change model and recompute

- Providers may emit change notifications (e.g., file watcher, HTTP polling). The environment provider typically does not emit by default and is treated as snapshot input.
- On any provider change, Cocoar recomputes all rules for all target types in order and atomically swaps the cache. Consumers see consistent snapshots.
- If your rule factories (options/query) depend on current config, provider instances/subscriptions are rebuilt during recompute so dynamic dependencies take effect.

### Required vs optional rules

- Each rule can be marked required or optional.
- Required: failures (e.g., missing file, HTTP error) cause the recompute to fail for that rule/type.
- Optional: failures are tolerated and the rule is skipped for that recompute.

### Ordering and dependencies

- Place dependency-producing rules before dependency-consuming rules.
- Rules may read any type’s current snapshot during recompute. Avoid circular dependencies across types or rules to prevent surprises.

Guidance for recompute-time reads
- GetRequiredConfig<T>() throws if T does not exist yet; use only if you guarantee T is produced earlier.
- GetConfig<T>() returns null if T does not exist; handle nulls explicitly when reading dependencies.
- For guaranteed existence, seed the dependency type with an explicit rule (e.g., a static provider/factory rule — see `Rules.Using.FromStatic`).

### Merge semantics and limits

- Last-write-wins, key-by-key merge of JSON objects using colon-key flattening.
- Arrays are replace-only by design; an array value replaces the prior value at that key (no merging).
- Keys follow `Section:Key` flattening during merge; final objects are unflattened before binding to your types.

## Getting started

### Packages (NuGet)

- `Cocoar.Configuration`
- `Cocoar.Configuration.AspNetCore` (optional)
- `Cocoar.Configuration.HttpPolling` (optional)
- `Cocoar.Configuration.MicrosoftAdapter` (optional)

### Working Examples

All examples in this README are validated by automated tests. Check out [`ReadmeExamplesTests.cs`](src/tests/Cocoar.Configuration.Tests/ReadmeExamplesTests.cs) to:
- See complete, working code with proper error handling
- Copy-paste tested examples into your IDE  
- Understand the full context with imports and setup
- Get intellisense and compile-time validation

### Badges

Add these to your repo once published:
- Build: GitHub Actions Status
- NuGet: package version badges for each package

## Dynamic dependency example

Later rules can read the in-progress configuration from the manager to parameterize themselves. Here a file (or any provider) provides a URL used by a subsequent HTTP rule.

```csharp
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider.Fluent;
using Cocoar.Configuration.HttpPolling.Fluent;

builder.Services.AddCocoarConfiguration([
    // Base settings providing the URL
    Rules.Using.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", "Remote"))
         .For<MyHttpPollingSettings>()
         .Required()
         .Build(),

    // HTTP rule reads the current URL from the manager during recompute
    Rules.Using.FromHttp(cm => new HttpPollingRuleOptions(
            urlPathOrAbsolute: cm.GetRequiredConfig<MyHttpPollingSettings>().Url,
            baseAddress: "https://example.com",
            pollInterval: TimeSpan.FromSeconds(5)
        ))
        .For<MyCfg>()
        .Build()
]);
```

## Providers at a glance

- **FileSourceProvider**: Read JSON files; debounce filesystem notifications; see provider README for options and samples
- **EnvironmentVariableProvider**: Map env vars with separators `__` and `:`; optional prefix filter  
- **HttpPollingProvider**: Poll a JSON endpoint; emit only on real payload change
- **MicrosoftConfigurationSourceProvider**: Plug any IConfigurationSource into rules

See the provider READMEs linked above for detailed options and examples.

## Contributing

Issues and PRs welcome. Please keep provider abstractions stable and deterministic (e.g., option keys for instance pooling) and follow the merge semantics described in ARCHITECTURE.md.

**📝 Documentation Quality**: All README examples are backed by automated tests. When contributing new examples or changing existing ones, please update the corresponding tests in [`ReadmeExamplesTests.cs`](src/tests/Cocoar.Configuration.Tests/ReadmeExamplesTests.cs) to ensure they remain accurate and functional.

---

For deeper details, examples, and roadmap, check src/Cocoar.Configuration/README.md and ARCHITECTURE.md.

## Known gaps and next steps

- Array merge semantics: Arrays currently replace prior values. Additional strategies (append/merge/custom) are under consideration.
- Null/empty handling: Edge cases (nulls and empty objects) will be documented precisely; current behavior follows JSON deserialization defaults after key merge.
- Change emissions: Environment provider does not emit changes by default (snapshot only). If you need change-driven recompute, combine with other providers.
- Circular dependencies: Rules can read any type’s current snapshot during recompute. Avoid cycles; detection/guardrails may be added.
- DI lifetimes: Resulting config types are singletons today; this may evolve.
