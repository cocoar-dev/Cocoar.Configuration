# Providers Overview

A provider is a data source that delivers configuration as JSON. Every rule connects a configuration type to a provider:

```csharp
rule.For<AppSettings>()              // What type to populate
    .FromFile("appsettings.json")    // Which provider to use
```

## The Provider Contract

All providers implement two methods:

| Method | Purpose |
|---|---|
| `FetchConfigurationBytesAsync()` | One-time fetch — returns UTF-8 JSON bytes |
| `ChangesAsBytes()` | Change stream — returns `IObservable<byte[]>` that emits when data changes |

Providers always return **raw UTF-8 bytes**, never strings. This avoids unnecessary allocations and keeps sensitive data out of managed string memory.

On failure, providers return an empty JSON object `{}` — never null. This means a failed optional rule contributes nothing, and values from earlier rules remain unchanged.

## Built-in Providers

| Provider | Fluent API | Reactive | Package |
|---|---|---|---|
| [File](/guide/providers/file) | `.FromFile("path")` | File watcher | Core |
| [Environment Variables](/guide/providers/environment) | `.FromEnvironment("PREFIX_")` | No | Core |
| [Command Line](/guide/providers/command-line) | `.FromCommandLine("--prefix")` | No | Core |
| [LocalStorage](/guide/providers/localstorage) | `.FromLocalStorage()` | Yes (on write) | Core |
| [Static JSON](/guide/providers/static-observable#static-json) | `.FromStaticJson("{...}")` | No | Core |
| [Observable](/guide/providers/static-observable#observable) | `.FromObservable(obs)` | Yes | Core |
| [HTTP](/guide/providers/http-polling) | `.FromHttp(url)` | Polling / SSE / one-time | Http |
| [Microsoft IConfiguration](/guide/providers/microsoft-adapter) | `.FromIConfiguration(config)` | IConfiguration reload token | MicrosoftAdapter |

**Reactive** means the provider can detect changes and trigger a recompute automatically. Environment variables, command-line arguments, and static JSON are inherently immutable during process lifetime — they're read once and don't change, so there's nothing to watch.

## Provider Lifecycle

Providers are managed by the rule engine:

1. **Created** when a rule first needs the provider
2. **Cached** by provider key — multiple rules sharing the same source (e.g., same file directory) reuse one provider instance
3. **Subscribed** for changes after the initial fetch
4. **Disposed** when no more rules reference the provider

The caching is key-based. For example, two rules reading different files from the same directory share one `FileSourceProvider` because the directory is the provider key. The filenames are query-level parameters.

## Provider vs Query

Each provider splits its configuration into two levels:

- **Provider options** — shared, instance-level settings (e.g., which directory to watch, HTTP base address)
- **Query options** — per-rule settings (e.g., which filename, which URL path, which env var prefix)

This split enables efficient resource sharing. One file watcher monitors an entire directory; individual rules query specific files within it.

## Building Your Own

If the built-in providers don't cover your use case, you can [build a custom provider](/guide/providers/custom) by extending `ConfigurationProvider<TOptions, TQuery>`.
