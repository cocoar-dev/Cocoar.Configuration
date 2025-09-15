# HttpPollingProvider

Periodically poll an HTTP endpoint returning JSON and merge into configuration.

- Options: `HttpPollingProviderOptions(HttpClient httpClient, TimeSpan? interval = null)`
- Query: `HttpPollingProviderQueryOptions(string path)` (path relative to base address)
- Rule construction now uses rule-level `.Select("Section:Sub")` rather than query-level configurationPath (removed).

## Features

- Periodic polling (default interval if not specified in options; configure in `HttpPollingProviderOptions`).
- Change detection: compares raw payload text to last payload; emits change if different.
- Subsequent rule-level pipeline: Fetch → Select (optional) → Mount (optional) → Merge.

## Basic Usage

```csharp
services.AddHttpClient("config", c => c.BaseAddress = new Uri("https://config.example.com/"));

services.AddCocoarConfiguration(
    Rule.From.HttpPolling(
        _ => HttpPollingRuleOptions.FromPath("service.json"),
        // provider options factory (http client + interval)
        sp => new HttpPollingProviderOptions(sp.GetRequiredService<IHttpClientFactory>().CreateClient("config"), TimeSpan.FromSeconds(30))
    ).Select("Service").For<ServiceSettings>().Build()
);
```

## Concise Usage

If you have registered an `HttpClient` named `config` and a default interval via options:

```csharp
services.AddCocoarConfiguration(
    Rule.From.HttpPolling("service.json").Select("Service").For<ServiceSettings>().Build()
);
```

## Mounting Example

```csharp
services.AddCocoarConfiguration(
    Rule.From.HttpPolling("feature.json")
        .Select("Feature")
        .MountAt("Features:Primary")
        .For<FeatureSettings>()
        .Build()
);
```

## Migration from Previous Version

Previously you might have written:

```csharp
Rule.From.HttpPolling(_ => HttpPollingRuleOptions.FromPath("service.json", configurationPath: "Service"))
    .For<ServiceSettings>()
    .Build();
```

Now write:

```csharp
Rule.From.HttpPolling("service.json")
    .Select("Service")
    .For<ServiceSettings>()
    .Build();
```

## Notes

- `.Select` is optional; omit it to bind the entire JSON root.
- `.MountAt` lets you relocate the (selected) subtree under a different root path before merge.
- HTTP errors surface on required rules during fetch; transient failures do not remove the last known good configuration— they only block updates until a successful poll.
