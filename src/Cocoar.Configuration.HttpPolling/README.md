# HttpPollingProvider

Fetch JSON over HTTP(S) on an interval and emit change signals only when the payload changes.

- Options: `HttpPollingProviderOptions(baseAddress?, pollInterval?, httpMessageHandler?)`
- Query: `HttpPollingProviderQueryOptions(urlPathOrAbsolute, sectionPath?, wrapperPath?, headers?)`
- Change semantics: polls at `pollInterval` and emits only when the fetched JSON differs from the last payload. Internally caches last payload per query and reuses a single HttpClient per provider instance.

## When to use

- Centralized config service backing multiple apps.
- Remote feature flags or operational toggles where polling is acceptable.

## Example

```csharp
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.HttpPolling.Fluent;
using Cocoar.Configuration.HttpPolling.Fluent.ProviderOptions;

services.AddCocoarConfiguration(
    Rules.Using.FromHttp(_ => new HttpPollingRuleOptions(
        urlPathOrAbsolute: "/v1/settings",
        baseAddress: "https://config.example.com",
        pollInterval: TimeSpan.FromSeconds(10)
    ))
    .For<MyRemoteSettings>()
    .When(() => true)
    .Optional()
);
```

## Notes

- For change-only recompute, ensure the remote endpoint returns unchanged JSON when values don’t change.
- Consider future push models (SSE/SignalR) when lower latency is needed.
- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- See the root `README.md` ("How it works") and `ARCHITECTURE.md` for merge semantics, recompute behavior, and dynamic dependencies.

Known gaps
- Arrays replace prior values; alternate strategies may be added.
