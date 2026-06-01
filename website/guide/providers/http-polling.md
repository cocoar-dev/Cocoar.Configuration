---
description: FromHttp provider — one-time fetch, polling, SSE, SSE-with-fallback, failure threshold, dynamic endpoints, client-certificate and encrypted-secret token auth
---

# HTTP Provider

The HTTP provider fetches configuration from a remote endpoint. It supports one-time fetch, periodic polling, and real-time Server-Sent Events (SSE).

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
    pollInterval: TimeSpan.FromMinutes(5))
```

::: info Package
Requires the `Cocoar.Configuration.Http` package:
```shell
dotnet add package Cocoar.Configuration.Http
```
:::

## Modes

### One-Time Fetch

Fetches configuration once at startup. No background polling, no persistent connection:

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features")
```

Good for: static remote config that doesn't change during process lifetime, or config that changes rarely enough that a restart is acceptable.

### Polling

Fetches configuration at regular intervals, detecting changes automatically:

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
    pollInterval: TimeSpan.FromMinutes(5))
```

1. Makes an initial HTTP GET to fetch configuration
2. Starts a background polling loop at the configured interval
3. On each poll, fetches the endpoint and emits the response bytes
4. The engine detects content changes and triggers a recompute when data differs

### Server-Sent Events (SSE)

Opens a persistent connection to the server. The server pushes updates in real time when configuration changes:

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
    serverSentEvents: true)
```

Good for: web-scale deployments where sub-second propagation matters. Works through proxies and load balancers. The provider handles reconnection automatically.

### SSE with Polling Fallback

Combines SSE for real-time updates with periodic polling as a safety net. If the SSE connection drops and reconnection fails, the provider falls back to polling:

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
    serverSentEvents: true,
    fallbackPollInterval: TimeSpan.FromMinutes(5))
```

Good for: production deployments where you want real-time updates but need a guaranteed fallback if the SSE connection is unreliable.

## Options

| Option | Default | Description |
|---|---|---|
| `url` | (required) | Absolute URL of the configuration endpoint |
| `pollInterval` | None | Time between poll requests. Omit for one-time fetch. |
| `serverSentEvents` | `false` | Enable SSE mode for real-time push updates |
| `fallbackPollInterval` | None | Polling interval to use when SSE connection fails |
| `headers` | None | Custom HTTP headers (e.g., API keys, auth tokens) |

## Error Handling

The provider tracks consecutive failures:

- Individual failures are logged but don't affect configuration
- After reaching the failure threshold (default 3 consecutive), the provider emits empty bytes `{}`
- This triggers health degradation for optional rules, or rollback for required rules
- On the next successful fetch, the failure counter resets

This prevents a single network blip from disrupting your app while still detecting sustained outages.

## Dynamic Endpoints <Badge type="info" text="ADV" />

Use the `IConfigurationAccessor` to derive URLs from earlier config:

```csharp
rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    rule.For<ApiConfig>().FromHttp(accessor =>
    {
        var tenant = accessor.GetConfig<TenantSettings>();
        return new HttpRuleOptions(
            $"https://{tenant.Region}.config.example.com/api",
            pollInterval: TimeSpan.FromMinutes(5));
    }),
]
```

When the tenant's region changes, the provider automatically switches to the new endpoint.

## Authentication <Badge type="info" text="ADV" />

### Certificate-Based Auth (Recommended)

Client certificate authentication avoids tokens in application memory. Pass a certificate via `HttpMessageHandler`:

```csharp
var cert = new X509Certificate2("certs/client.pfx");

var handler = new HttpClientHandler();
handler.ClientCertificates.Add(cert);

rule.For<AppSettings>().FromHttp("https://config.example.com/settings",
    pollInterval: TimeSpan.FromMinutes(5),
    handler: handler)
```

Use password-less certificates — see [Working with Certificates](/guide/certificates) for why and how to protect them.

The `HttpMessageHandler` parameter gives you full control over the HTTP pipeline — use it for mutual TLS, custom retry policies, or any other `DelegatingHandler` chain.

### Token from Encrypted Secret

If the server requires a bearer token or API key, load it from an encrypted `Secret<T>` in an earlier rule. This keeps the token encrypted at rest and decrypted only at startup:

```csharp
rule => [
    rule.For<ApiSecrets>().FromFile("secrets.json").Required(),

    rule.For<AppSettings>().FromHttp(accessor =>
    {
        using var lease = accessor.GetConfig<ApiSecrets>()!.AuthToken.Open();
        return new HttpRuleOptions(
            "https://config.example.com/settings",
            pollInterval: TimeSpan.FromMinutes(5),
            headers: new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {lease.Value}"
            });
    })
]
```

:::warning Token becomes a string
Once the token is placed in an HTTP header, it exists as a `string` in memory — managed by `HttpClient`, outside of Cocoar's control. This is unavoidable for header-based authentication. For maximum security, prefer certificate-based auth where no secret enters managed string memory.
:::

### Plain Headers

For non-sensitive headers (API version, tenant ID, etc.):

```csharp
rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
    pollInterval: TimeSpan.FromMinutes(5),
    headers: new Dictionary<string, string>
    {
        ["X-Api-Version"] = "2",
        ["X-Tenant-Id"] = "tenant-123"
    })
```

## Common Pattern

Remote config with local fallback:

```csharp
rule => [
    rule.For<FeatureConfig>().FromFile("features-defaults.json").Required(),
    rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
        pollInterval: TimeSpan.FromMinutes(5)),
]
```

The file provides defaults. The HTTP endpoint overrides what it sets. If the endpoint goes down, the defaults remain active.
