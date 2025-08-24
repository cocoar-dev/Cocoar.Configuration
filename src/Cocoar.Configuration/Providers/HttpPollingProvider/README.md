# HttpPollingProvider

Fetch JSON over HTTP(S) on an interval and emit change signals only when the payload changes.

- Options: `HttpPollingProviderOptions(baseAddress?, pollInterval?, httpMessageHandler?)`
- Query: `HttpPollingProviderQueryOptions(urlPathOrAbsolute, memberPath?, memberWrapper?)`
- Change semantics: polls at `pollInterval` and emits only when the fetched JSON differs from the last payload. Internally caches last payload per query and reuses a single HttpClient per provider instance.

## When to use

- Centralized config service backing multiple apps.
- Remote feature flags or operational toggles where polling is acceptable.

## Example

```csharp
using Cocoar.Configuration.Providers.HttpPollingProvider;

services.AddCocoarConfiguration(
    HttpPollingProvider.CreateRule<MyRemoteSettings, MyRemoteSettings>(
        optionsFactory: _ => new HttpPollingProviderOptions(
            baseAddress: "https://config.example.com",
            pollInterval: TimeSpan.FromSeconds(10)
        ),
        queryFactory: _ => new HttpPollingProviderQueryOptions(
            urlPathOrAbsolute: "/v1/settings",
            memberPath: "MyRemote"
        ),
        useWhen: () => true,
        required: false
    )
);
```

## Notes

- For change-only recompute, ensure the remote endpoint returns unchanged JSON when values don’t change.
- Consider future push models (SSE/SignalR) when lower latency is needed.
- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- See the root `README.md` and `ARCHITECTURE.md` for merge semantics and the overall change model.
