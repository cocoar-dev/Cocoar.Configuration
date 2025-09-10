# Cocoar.Configuration — Architecture & Status (2025-09-10)

This document captures the current design, behavior, and implementation details to onboard quickly and to guide future work.

## Goals

- Aggregate configuration from multiple sources into strongly-typed objects.
- Deterministic rule ordering: later rules override earlier ones (last-write-wins per key).
- Live updates: source changes trigger a full recompute; consumers get latest values on next retrieval.
- Simple DI integration for console/web apps.

## Key Concepts

- ConfigRule: a single step in the pipeline describing
  - ProviderType (e.g., File, Environment, HTTP)
  - ProviderOptions (instance options; define resource identity and lifetime)
  - QueryOptions (what to fetch/select from the provider)
  - ConfigTypeDefinition (contract+optional implementation type)
  - Options (UseWhen, Required)
- Factory deferral: rule factories (instance/query option factories) are stored, not executed at rule construction time. They are invoked during recompute, enabling dynamic dependencies between rules.
- Providers
  - Implement GetValueAsync(query) and Changes(query)
  - Startup behavior: ConfigManager calls GetValueAsync for every rule immediately (no waiting for polls/watchers). Changes() streams do not emit an initial value; they only emit on subsequent source changes.
  - Instance options live in the provider constructor; queries are per-call to allow reuse and dynamic binding.
- Dynamic dependencies: during recompute, later rules can read earlier outputs via ConfigManager (e.g., GetRequiredConfig<T>), thanks to a working snapshot that is updated after each rule’s merge.
- Architecture diagram
  - See the visual diagram in `architecture.drawio` (open with diagrams.net/draw.io).
  - Quick Mermaid overview:

```mermaid
flowchart LR
  App["App / DI consumers"] -->|GetConfig<T>| CM[ConfigManager]
  CM -->|manages| RM[RuleManager (per rule)]
  RM -->|Acquire (ProviderType, CalculateKey)| PR[ProviderRegistry]
  subgraph Providers
    FSP[FileSourceProvider]
    EVP[EnvironmentVariableProvider]
    HPP[HttpPollingProvider]
  end
  PR -->|ProviderHandle| Providers
  RM -->|GetValue/Changes with QueryOptions| Providers
  Providers -->|JSON object| RM
  RM -->|flatten & merge| CM
  Providers -->|Changes (IObservable)| RM -->|Recompute| CM
```
- ConfigManager
  - Holds ordered rules and orchestrates a per-rule RuleManager
  - Recompute: merge flattened JSON objects; later rules win per key
  - On change (any provider): recompute all rules; RuleManagers manage provider reuse/subscriptions keyed by options/query
  - Required rule: throws on failure; optional rules are skipped with a warning

### Recompute & working snapshot

- Full recompute: on any change notification, all rules are re-evaluated in order; outputs are merged last-write-wins per key.
- Working snapshot: while recompute is in progress, ConfigManager exposes an in-progress snapshot so that a later rule’s factory or provider options can read values emitted by earlier rules in the same recompute. After recompute finishes, the working snapshot is cleared and the final snapshot is swapped in atomically.

## Current Providers

- FileSourceProvider
  - Options: directory + optional debounce for the internal watcher
  - Query: filename, optional sectionPath/wrapperPath
  - Behavior: maintains a folder watcher; per filename change stream; caches last JSON per file
  - Watcher errors during Changes() are swallowed to keep stream alive; GetValueAsync still throws on missing file (so Required rules fail properly in recompute). No initial emission from Changes().
- EnvironmentVariableProvider
  - Options: optional prefix
  - Query: optional sectionPath/wrapperPath (prefix concept is mapped to sectionPath)
  - Behavior: snapshot read via GetValueAsync at startup; Changes() is a no-op (does not emit initially).
- HttpPollingProvider
  - Options: optional baseAddress, pollInterval, optional HttpMessageHandler (for tests)
  - Query: urlPathOrAbsolute, optional sectionPath/wrapperPath, optional headers
  - Behavior: single HttpClient per provider; GetValueAsync fetches immediately at startup; Changes() polls on interval and emits only when payload actually changes; caches last value per query key to avoid duplicate recompute on immediate reads
- MicrosoftConfigurationSourceProvider (Adapter)
  - Wraps Microsoft.Extensions.Configuration sources (JSON, INI, environment variables, etc.) and adapts them to this system.
  - Honors reload-on-change via Microsoft change tokens; environment variables typically don’t emit changes.
  - Query supports keyPrefix and optional wrapperPath.
- StaticJsonProvider
  - Supplies a static JSON value (explicit seeding) and never emits changes.
  - Useful to seed dependent rules or provide defaults via fluent Rules.FromStatic.

## Merge Semantics

- Each provider returns a JSON object. Objects are flattened into colon-separated keys (e.g., SectionA:Enabled).
- A rule's output overrides existing keys for its config type.
- After all rules, unflatten back to a JSON object and deserialize to the desired type.
- Arrays: not merged; only object graphs are considered in the flatten/unflatten process.

## DI & Access

- ServiceCollection extension registers ConfigManager as singleton and exposes requested config contracts (and implementation types when declared). Registration uses GetRequiredConfig to ensure mandatory presence at startup.
- Access via ConfigManager:
  - GetConfig<T>() / GetConfig(Type): returns current snapshot or null when missing.
  - GetRequiredConfig<T>() / GetRequiredConfig(Type): throws InvalidOperationException when missing.
  - TryGetConfig<T>(out T?) / TryGetConfig(Type, out object?): convenience for null-safe checks.

## Logging

- Internal minimal logger interfaces wired in ConfigManager for events: recompute start/finish, rule skip, optional/required failures, and trigger errors.

## Error Handling

- Required rules fail recompute with an InvalidOperationException (wrapped original exception).
- Optional rules are skipped on error; recompute proceeds.
- Change streams attempt to avoid faulting (File: swallow IO in Changes; HTTP: emit only on success and when changed).

## Known Trade-offs & Future Improvements

- Recompute scope: full recompute is simple and correct but not minimal.
  - Future: partial recompute from the changed rule to the end.
- Provider reuse across recomputes:
  - Implemented via RuleManager: providers are reused when instance options key is unchanged; subscriptions are refreshed when query key changes.
  - Optional IDisposable disposal hooks are honored when a provider is replaced.
- Arrays merging: consider strategies (overwrite/append/custom policy).
- Naming consistency and nullability cleanliness (memberPath vs prefix).
- Provider contract variants:
  - Optional BoundProvider layer to pin a provider to a specific query for stricter API (parameterless GetValue/Changes) without losing reuse.
- Cycle detection/diagnostics for dynamic dependencies: warn or trace potential cycles; currently not enforced.

## Testing Status

- Unit tests for File, Environment, HTTP providers; integration tests for Microsoft adapter; end-to-end dynamic dependency test; static seeding tests.
- Full solution tests: green as of 2025-09-10 (41 tests).

## Usage Examples

- File + Env + HTTP overlay with DI:

```csharp
services.AddCocoarConfiguration(
  Rules.FromFile(_ => FileSourceRuleOptions.FromFilePath("./config.json", "SectionA")).For<MySectionSettings>(),
  Rules.FromEnvironment(_ => new EnvironmentVariableRuleOptions(keyPrefix: "MYAPP_")).For<MySectionSettings>(),
  // Generic order: Concrete type first, optional interface second
  Rules.Using.FromHttp(_ => new HttpPollingRuleOptions(
        optionsFactory: _ => new HttpPollingProviderOptions("https://config.example.com", TimeSpan.FromSeconds(10)),
  queryFactory: _ => new HttpPollingProviderQueryOptions("/v1/settings", sectionPath: "SectionA")
    )
);
```

## Quick Reference (Contracts)

- ConfigRuleOptions: UseWhen (Func<bool>), Required (bool)
- ConfigTypeDefinition: ConfigType, ImplementationType?
- Provider base: ConfigSourceProvider<TInstanceOptions, TQueryOptions>
- File options/query: FileSourceProviderOptions(dir, debounceTime?), FileSourceProviderQueryOptions(filename, sectionPath?, wrapperPath?)
- Env options/query: EnvironmentVariableProviderOptions(prefix?), EnvironmentVariableProviderQueryOptions(memberPath?, memberWrapper?)
  - Nesting separators: "__" (double underscore), ":", and ".". Single '_' is treated as a literal.
- HTTP options/query: HttpPollingProviderOptions(baseAddress?, interval?, handler?), HttpPollingProviderQueryOptions(urlPathOrAbsolute, sectionPath?, wrapperPath?)

## Version

- Commit date: 2025-09-10
- Branch: fix/pipeline
