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

### 5) Fluent API (generic and extensible)

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
- Providers may emit only on real changes (HTTP) or debounce noisy signals (File). Environment currently doesn’t emit by default.
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
