# Cocoar.Configuration

A lightweight, strongly-typed configuration aggregator for .NET apps.

- Load from multiple sources (JSON files, environment variables, HTTP via separate package, or any Microsoft IConfigurationSource via an adapter)
- Merge hierarchically with last-write-wins semantics
- Watch JSON files for changes (debounced) and update source cache
- Live recompute: on any provider change, all rules are recomputed in order (last rule wins)
- Simple DI integration for generic retrieval; ASP.NET Core builder extension included

See ARCHITECTURE.md for a deeper dive into design, merge semantics, providers, and roadmap.

This package is split into:
- Cocoar.Configuration (core): ConfigManager, providers, merge logic
- Cocoar.Configuration.AspNetCore: WebApplicationBuilder integration helpers

## Features

- Rules-based configuration assembly via `ConfigRule`
- File provider with filesystem watcher and debounce
- Environment variable provider with optional prefix filtering; supports `__` and `:` for nesting (single `_` is literal)
- HTTP polling provider (separate package) that emits only on real payload changes; optional request headers
- Map to interface or concrete types via `ConfigTypeDefinition`
- String-to-primitive JSON converter to coerce "true", "42", etc. when values are strings
- Dynamic rule factories (options/query derived from current config state)
- Required/optional rule handling per rule, with exceptions on required failures

## Key concepts

- ConfigRule: describes one source and how to query it
- Provider: returns JSON payloads and optional change notifications
- Merge: JSON objects are flattened to colon keys (e.g., SectionA:Enabled) and merged; later rules overwrite earlier ones
- ConfigTypeDefinition: which type the assembled config should be deserialized into

## Packages/namespaces

- `Cocoar.Configuration` (core)
- `Cocoar.Configuration.Providers.FileSourceProvider`
- `Cocoar.Configuration.Providers.EnvironmentVariableProvider`
- `Cocoar.Configuration.HttpPolling` (separate package)
- `Cocoar.Configuration.Extensions` (DI for ServiceCollection)
- `Cocoar.Configuration.AspNetCore` (WebApplicationBuilder extension)

## Quick start

### 1) Define your settings contract

```csharp
public interface IMySectionSettings
{
    bool Enabled { get; }
    int Value { get; }
}

public sealed class MySectionSettings : IMySectionSettings
{
    public bool Enabled { get; set; }
    public int Value { get; set; }
}
```

### 2) Create rules and build a ConfigManager

```csharp
using Cocoar.Configuration;
using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

var rules = new []
{
    // Load SectionA from a JSON file
    // Generic order: Concrete type first, optional interface second
    Rules.FromFile(_ => FileSourceRuleOptions.FromFilePath(
    filepath: "./appsettings.local.json",
    sectionPath: "SectionA",
        debounceTime: TimeSpan.FromMilliseconds(150)
    ),
    // Overlay with environment variables (e.g., Enabled=false)
    Rules.FromEnvironment(_ => new EnvironmentVariableRuleOptions()).For<MySectionSettings>().As<IMySectionSettings>()
};

var manager = new ConfigManager(rules).Initialize();
var cfg = manager.GetConfig<IMySectionSettings>();
```

### 3) DI integration (console/worker)

```csharp
using Cocoar.Configuration.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddCocoarConfiguration(rules);

var sp = services.BuildServiceProvider();
var manager = sp.GetRequiredService<ConfigManager>();
var cfg = manager.GetConfig<IMySectionSettings>();
```

### 4) ASP.NET Core integration

```csharp
using Cocoar.Configuration.AspNetCore;

var builder = WebApplication.CreateBuilder(args)
    .AddCocoarConfiguration(rules);

var cfg = builder.GetCocoarConfiguration<IMySectionSettings>();
```

### 5) Service Lifetimes & Keyed Services

Control how configuration types are registered in DI using the `.As<TInterface>()` method:

```csharp
using Microsoft.Extensions.DependencyInjection;

var rules = new[]
{
    // Simple interface registration (defaults to Singleton)
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Database"))
        .For<DatabaseConfig>()
        .As<IDatabaseConfig>()  // Registered as Singleton
        .Build(),
        
    // Direct concrete type registration with lifetime
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Cache"))
        .For<CacheConfig>(ServiceLifetime.Scoped)  // Register concrete type as Scoped
        .Build(),
        
    // Explicit lifetime control for interfaces
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Logging"))
        .For<LoggingConfig>()
        .As<ILoggingConfig>(ServiceLifetime.Scoped)  // Scoped lifetime
        .Build(),
        
    // Mixed: Concrete type + Interface with different lifetimes
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Mixed"))
        .For<DatabaseConfig>(ServiceLifetime.Scoped)      // Concrete as Scoped
        .As<IDatabaseConfig>(ServiceLifetime.Singleton)   // Interface as Singleton  
        .Build(),
        
    // Keyed services for multiple configurations
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Primary"))
        .For<DatabaseConfig>(ServiceLifetime.Singleton, "primary-concrete")
        .As<IDatabaseConfig>(ServiceLifetime.Singleton, "primary-interface")
        .Build(),
        
    Rules.FromFile(opts => FileSourceRuleOptions.FromFilePath("config.json", "Secondary"))  
        .For<DatabaseConfig>()
        .As<IDatabaseConfig>(ServiceLifetime.Singleton, "secondary")
        .Build()
};

// Usage in DI
services.AddCocoarConfiguration(rules);

// Resolve by key
var primaryConcrete = serviceProvider.GetRequiredKeyedService<DatabaseConfig>("primary-concrete");
var primaryInterface = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("primary-interface");
var secondary = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("secondary");

// Regular resolution (no keys)
var cache = serviceProvider.GetRequiredService<CacheConfig>();       // Scoped
var logging = serviceProvider.GetRequiredService<ILoggingConfig>();  // Scoped
```

### 6) Fluent API (generic and extensible)

You can define rules with a fluent syntax. There are two ways:

- Provider-specific helpers: `Rules.FromFile(...)`, `Rules.FromEnvironment(...)`. HTTP is available via extension: `Rules.Using.FromHttp(...)` when referencing Cocoar.Configuration.HttpPolling.
- Generic entry point for any provider: `Rules.FromProvider<TProvider, TInstanceOptions, TQueryOptions>(...)`.

Example (generic):

```csharp
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;

var rules = new[]
{
    Rules.FromProvider<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            instance: _ => new FileSourceProviderOptions(directory: ".", debounceTime: TimeSpan.FromMilliseconds(100)),
            query:    _ => new FileSourceProviderQueryOptions(filename: "appsettings.json", sectionPath: "SectionA"))
    .For<MySectionSettings>()
        .Required()
        .Build(),
};
```

Or, with the Microsoft adapter (separate package):

```csharp
using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.Configuration;
using Cocoar.Configuration.MicrosoftAdapter;

var rules = new[]
{
    // Microsoft IConfigurationSource adapter (bring your own source)
    Rules.FromProvider<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
            instanceOptions: _ => new MicrosoftConfigurationSourceProviderOptions(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                {
                    ["My:Section:Enabled"] = "true",
                    ["My:Section:Value"] = "42",
                }).Sources[0]
            ),
            queryOptions: _ => new MicrosoftConfigurationSourceProviderQueryOptions(keyPrefix: "My:Section"))
    .For<MySectionSettings>()
        .Optional()
        .Build(),

    Rules.FromFile(_ => new FileSourceRuleOptions(
            filepath: "appsettings.json",
            sectionPath: "SectionA",
            debounceTime: TimeSpan.FromMilliseconds(100)))
    .For<MySectionSettings>()
        .Optional()
        .Build(),
};
```

## Providers

### Change model (all providers)

- Providers expose a change stream used as a trigger. When any provider emits a change, the manager recomputes all rules in order and atomically swaps the cache.
- Providers may emit only on real changes (HTTP) or debounce noisy signals (File). Environment currently doesn't emit by default.
- After each recompute, provider instances and subscriptions are rebuilt to honor dynamic factories (option/query lambdas).

### FileSourceProvider

- Options: `FileSourceProviderOptions(directory, debounceTime)`
- Query: `FileSourceProviderQueryOptions(filename, sectionPath?, wrapperPath?, debounceTime?)`
- Factory:

```csharp
Rules.FromFile(cm => new FileSourceRuleOptions(
    filepath: "./config.json",
    sectionPath: "SectionA",   // optional: pick a section of the JSON
    wrapperPath: null,           // optional: wrap result under a property name
    debounceTime: TimeSpan.FromMilliseconds(100) // optional
);
```

Watches the folder for changes and emits updates per file with optional per-file debounce.

### EnvironmentVariableProvider

- Options: `EnvironmentVariableProviderOptions(keyPrefix?)`
- Query: `EnvironmentVariableProviderQueryOptions(keyPrefix?, wrapperPath?)`
- Factory:

```csharp
// No prefix: includes all environment variables
Rules.FromEnvironment(_ => new EnvironmentVariableRuleOptions()).For<TConcrete>().As<TInterface>();

// Prefix: include only variables that start with the prefix
// Nesting separators: "__" (double underscore) and ":". Single '_' is literal.
// Example: MYAPP__Logging__Level=Debug → { "Logging": { "Level": "Debug" } }
var rule = Rules.FromEnvironment(_ => new EnvironmentVariableRuleOptions(keyPrefix: "MYAPP")).For<TConcrete>().As<TInterface>().Build();
```

Notes:
- When a prefix is provided, variables are exposed without the prefix (e.g., `MYAPP_FOO` -> `FOO`).
- Values are strings at source; the StringToPrimitiveConverter can coerce to bool/int/etc. during deserialization.

### HttpPollingProvider (separate package)

- Options: `HttpPollingProviderOptions(baseAddress?, pollInterval?, handler?)`
- Query: `HttpPollingProviderQueryOptions(urlPathOrAbsolute, sectionPath?, wrapperPath?, headers?)`
- Factory (static or lambda-based):

```csharp
using Cocoar.Configuration.HttpPolling; // from Cocoar.Configuration.HttpPolling package

services.AddCocoarConfiguration(
    Rules.Using.FromHttp(_ => new HttpPollingRuleOptions(
        optionsFactory: _ => new HttpPollingProviderOptions(
            baseAddress: "https://config.example.com",
            pollInterval: TimeSpan.FromSeconds(10)
        ),
    queryFactory: _ => new HttpPollingProviderQueryOptions("/v1/settings", sectionPath: "MyRemote"),
        useWhen: () => true
    )
);
```

Notes:
- The change stream polls and emits only when the fetched payload actually changes. That means `ConfigManager` recomputes only on real changes.
- Combine with `UseWhen` and other rules to layer remote config over files/env.
- When using the fluent API, you can supply headers:

```csharp
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;

var rules = new []
{
    // requires: using Cocoar.Configuration.HttpPolling.Fluent; and package reference
    Rules.Using.FromHttp(_ => new HttpPollingRuleOptions(
        urlPathOrAbsolute: "/v1/settings",
    sectionPath: "MyRemote",
        baseAddress: "https://config.example.com",
        pollInterval: TimeSpan.FromSeconds(10),
        headers: new Dictionary<string,string> { ["Authorization"] = "Bearer abc" }
    ))
    .For<MySettings>()
    .Build()
};
```

## Merge semantics

- Each provider returns a JSON object. Objects are flattened to colon-keys (e.g., `SectionA:Enabled`).
- Flat maps are merged in rule order; later rules override earlier ones key-by-key.
- The final flat map is unflattened and deserialized into the requested type.

Limitations:
- Arrays are not merged—only JSON objects are considered in the flatten/unflatten process.

## Public API (core)

- `ConfigManager.Initialize()`
- `T? ConfigManager.GetConfig<T>()`
- `object? ConfigManager.GetConfig(Type type)`
- `bool ConfigManager.TryGetConfig<T>(out T? value)`
- `bool ConfigManager.TryGetConfig(Type type, out object? value)`
- `T ConfigManager.GetRequiredConfig<T>()`
- `object ConfigManager.GetRequiredConfig(Type type)`
- `JsonElement? ConfigManager.GetConfigAsJson(Type type)`
- `ConfigRule.Create<TProvider, TOptions, TQueryOptions>(...)`
- Provider factories:
    - Use the fluent DSL via Rules.FromX(...)

## API semantics

- GetConfig<T>() / GetConfig(Type):
    - Returns the current snapshot value or null when the config is missing or cannot be deserialized.
    - Never throws for missing values. Use this for optional reads.
- GetRequiredConfig<T>() / GetRequiredConfig(Type):
    - Returns the current snapshot value.
    - Throws InvalidOperationException if the config is missing. Use this when a rule or DI registration depends on it.
- TryGetConfig<T>(out T?) / TryGetConfig(Type, out object?):
    - Returns true only when a non-null value is available and assigns it to the out parameter; otherwise returns false and sets out to null.
- GetConfigAsJson(Type):
    - Returns the merged JSON for the given type, or null if not present.
    - Helpful for diagnostics or custom deserialization.

## Examples

### Merge two files

```csharp
services.AddCocoarConfiguration(
    Rules.FromFile(_ => FileSourceRuleOptions.FromFilePath("./config1.json", "SectionA")).For<MySectionSettings>(),
    Rules.FromFile(_ => FileSourceRuleOptions.FromFilePath("./config2.json", "SectionA")).For<MySectionSettings>()
);

var settings = sp.GetRequiredService<ConfigManager>().GetConfig<MySectionSettings>();
```

### Env vars override file

```csharp
// JSON contains SectionA.Enabled=true
// Environment contains Enabled=false
services.AddCocoarConfiguration(
    // Generic order: Concrete first, optional interface second
    Rules.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", "SectionA")).For<MySectionSettings>().As<IMySectionSettings>(),
    Rules.FromEnvironment(_ => new EnvironmentVariableRuleOptions()).For<MySectionSettings>().As<IMySectionSettings>()
);

var result = sp.GetRequiredService<ConfigManager>().GetConfig<IMySectionSettings>();
// result.Enabled == false
```

### Seed + dependent rule (FromStatic + GetRequiredConfig)

Seed one type explicitly, then have a dependent rule read it during recompute.

```csharp
public class MySeed { public string Name { get; set; } = "Seed"; public int Value { get; set; } = 1; }
public class Container { public MySeed? Dep { get; set; } }

var rules = new[]
{
    // 1) Seed MySeed via a static provider (no change emissions)
    Rules.FromStatic<MySeed>(_ => new MySeed { Name = "Seed", Value = 11 })
        .ForType<MySeed>()
        .Build(),

    // 2) Dependent rule: reads the seeded type at recompute time
    Rules.FromStatic<MySeed>(cm => cm.GetRequiredConfig<MySeed>(), wrapperPath: "Dep")
        .ForType<Container>()
        .Build(),
};

var manager = new ConfigManager(rules).Initialize();
var c = manager.GetRequiredConfig<Container>();
// c.Dep is non-null and contains the seeded value
```

## Notes and current limitations

- Recompute model: on any change emission, the manager recomputes all rules from scratch (ordered merge). This is simple and correct but not minimal; a future optimization could recompute only affected rules.
- Provider lifecycle: managed by an internal RuleManager per rule which reuses providers when instance options don't change and rebuilds subscriptions when queries change. This enables dynamic factories without recreating instances unnecessarily.
- Arrays in merge: arrays are not flattened/merged; objects only.
- Nullability warnings: some parameters accept null but are non-nullable; can be tidied up.
- Naming consistency: minor inconsistencies (e.g., env prefix as MemberPath) can be aligned later.
 - For a full overview and roadmap, see ARCHITECTURE.md.

## Tested environment

- .NET SDK 9.0.304 / Runtimes 8.0–10.0 preview present
- All tests in `Cocoar.Configuration.Tests` passed locally; full solution tests green

## Roadmap ideas

- Partial recompute (from the changed rule to the end) to reduce work on frequent changes
- Provider lifecycle reuse across recomputes (pooling; `IDisposable` support for long-lived connections)
- Array merge strategies (replace/append/custom)
- Clean up nullability and naming consistency (prefix/memberPath)
- Additional providers: HTTP SSE/SignalR live streams, timer-less push models

---

For ASP.NET Core usage, prefer the `Cocoar.Configuration.AspNetCore` extension for a builder-first experience. For non-web apps, use the `Cocoar.Configuration.Extensions` DI helpers.

## Fluent extensibility for third‑party providers

To let external provider packages add their own fluent entry points without modifying this repo, the fluent host exposes an instance handle `Rules.Using` that can be the target of extension methods.

In your provider package (separate project), define an extension like this:

```csharp
using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Abstractions;

namespace MyCompany.Configuration.MyProvider;

public static class MyProviderRulesExtensions
{
    // Option A: expose the generic builder directly
    public static ProviderRuleBuilder<MyProvider, MyProviderOptions, MyProviderQueryOptions> FromMyProvider(
        this Rules.Dsl _,
        Func<ConfigManager, MyProviderOptions> instance,
        Func<ConfigManager, MyProviderQueryOptions> query)
        => Rules.FromProvider<MyProvider, MyProviderOptions, MyProviderQueryOptions>(instance, query);

    // Option B: offer a convenience wrapper with fewer parameters
    public static ProviderRuleBuilder<MyProvider, MyProviderOptions, MyProviderQueryOptions> FromMyProvider(
        this Rules.Dsl _, string endpoint, TimeSpan? pollInterval = null)
        => Rules.FromProvider<MyProvider, MyProviderOptions, MyProviderQueryOptions>(
            instance: _ => new MyProviderOptions { PollInterval = pollInterval ?? TimeSpan.FromSeconds(10) },
            query:    _ => new MyProviderQueryOptions { Url = endpoint });
}
```

Consumers can then write:

```csharp
using Cocoar.Configuration.Fluent;
using MyCompany.Configuration.MyProvider; // brings in the extension method

var rules = new[]
{
    Rules.Using
        .FromMyProvider("https://config.example.com/api/v1/settings", TimeSpan.FromSeconds(15))
    .For<MySettings>()
        .Required()
        .Build(),
};
```

Notes:
- `Rules.Using` is a lightweight instance purely for extension methods; it carries no state.
- Under the hood, your extension can delegate to `Rules.FromProvider<...>()`, which wires into the provider pooling/orchestration.
- Your provider types should implement the abstractions: `ConfigSourceProvider<TInstanceOptions,TQueryOptions>`, `ISourceProviderInstanceOptions`, and `ISourceProviderQueryOptions`.

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
