# Cocoar.Configuration — Architecture & Status (2025-08-09)

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
- Providers
  - Implement GetValueAsync(query) and Changes(query)
  - Instance options live in the provider constructor; queries are per-call to allow reuse and dynamic binding.
- ConfigManager
  - Holds ordered rules and orchestrates a per-rule RuleManager
  - Recompute: merge flattened JSON objects; later rules win per key
  - On change (any provider): recompute all rules; RuleManagers manage provider reuse/subscriptions keyed by options/query
  - Required rule: throws on failure; optional rules are skipped with a warning

## Current Providers

- FileSourceProvider
  - Options: directory + optional debounce for the internal watcher
  - Query: filename, optional memberPath/memberWrapper
  - Behavior: maintains a folder watcher; per filename change stream; caches last JSON per file
  - Watcher errors during Changes() are swallowed to keep stream alive; GetValueAsync still throws on missing file (so Required rules fail properly in recompute)
- EnvironmentVariableProvider
  - Options: optional prefix
  - Query: optional memberPath/memberWrapper (prefix concept is mapped to memberPath)
  - Behavior: snapshot read; change stream intentionally does not emit initial values (used as trigger only if implemented in future)
- HttpPollingProvider
  - Options: optional baseAddress, pollInterval, optional HttpMessageHandler (for tests)
  - Query: urlPathOrAbsolute, optional memberPath/memberWrapper, optional headers
  - Behavior: single HttpClient per provider; polls on interval and emits only when payload actually changes; caches last value per query key to avoid duplicate recompute on immediate reads

## Merge Semantics

- Each provider returns a JSON object. Objects are flattened into dotted keys.
- A rule's output overrides existing keys for its config type.
- After all rules, unflatten back to a JSON object and deserialize to the desired type.
- Arrays: not merged; only object graphs are considered in the flatten/unflatten process.

## DI & Access

- ServiceCollection extension registers ConfigManager as singleton and exposes requested config contracts (and implementation types when declared).
- Access via ConfigManager.GetConfig<T>() or GetConfig(Type).

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
- Initial-immediate tick for HTTP:
  - Option to emit immediately on start (fetch once) to avoid waiting for the first interval.

## Testing Status

- Unit tests for File and Environment providers, change notifications, and integration.
- HTTP provider tests using Fake/Queue handlers; verify change-only behavior and recompute integration.
- Full solution tests: green as of 2025-08-08.

## Usage Examples

- File + Env + HTTP overlay with DI:

```csharp
services.AddCocoarConfiguration(
    FileSourceProvider.CreateRule<MySectionSettings>("./config.json", "SectionA"),
    EnvironmentVariableProvider.CreateRule<MySectionSettings>(memberPath: "MYAPP_"),
  // Generic order: Concrete type first, optional interface second
  HttpPollingProvider.CreateRule<MySectionSettings, MySectionSettings>(
        optionsFactory: _ => new HttpPollingProviderOptions("https://config.example.com", TimeSpan.FromSeconds(10)),
        queryFactory: _ => new HttpPollingProviderQueryOptions("/v1/settings", memberPath: "SectionA")
    )
);
```

## Quick Reference (Contracts)

- ConfigRuleOptions: UseWhen (Func<bool>), Required (bool)
- ConfigTypeDefinition: ConfigType, ImplementationType?
- Provider base: ConfigSourceProvider<TInstanceOptions, TQueryOptions>
- File options/query: FileSourceProviderOptions(dir, debounce?), FileSourceProviderQueryOptions(filename, memberPath?, memberWrapper?)
- Env options/query: EnvironmentVariableProviderOptions(prefix?), EnvironmentVariableProviderQueryOptions(memberPath?, memberWrapper?)
  - Nesting separators: "__" (double underscore), ":", and ".". Single '_' is treated as a literal.
- HTTP options/query: HttpPollingProviderOptions(baseAddress?, interval?, handler?), HttpPollingProviderQueryOptions(urlPathOrAbsolute, memberPath?, memberWrapper?)

## Version

- Commit date: 2025-08-08
- Branch: main
