---
description: Cocoar.Configuration.ConfigHub provider — authenticated snapshots, ETag validation, SSE invalidation, reconnect reconciliation, polling fallback, and dynamic endpoints
---

# ConfigHub Provider

`Cocoar.Configuration.ConfigHub` connects an application to a ConfigHub delivery endpoint. It is a ConfigHub-specific integration; use the [generic HTTP provider](/guide/providers/http-polling) for other HTTP configuration services.

```shell
dotnet add package Cocoar.Configuration.ConfigHub
```

```csharp
using Cocoar.Configuration.ConfigHub;

builder.AddCocoarConfiguration(configuration => configuration
    .UseConfiguration(rule =>
    [
        rule.For<AppSettings>().FromFile("appsettings.json"),
        rule.For<AppSettings>().FromConfigHub(
            "https://config.example/api/config/my-app",
            Environment.GetEnvironmentVariable("CONFIGHUB_DELIVERY_TOKEN")!),
    ]));
```

The local file supplies defaults. The later ConfigHub layer overrides the properties present in its snapshot and participates in the normal atomic merge and notification pipeline.

## Delivery Protocol

The provider treats the JSON snapshot as the only authoritative configuration:

1. It fetches the endpoint with `Accept: application/json` and `Authorization: Bearer <delivery-token>`.
2. It stores the response ETag and opens the same endpoint with `Accept: text/event-stream`.
3. An SSE event containing a `data` field invalidates the snapshot; the event payload itself is never parsed as configuration.
4. The provider conditionally refetches with `If-None-Match`. A `304 Not Modified` produces no configuration update.
5. Every SSE reconnect starts with a conditional refetch, closing the gap for events missed while disconnected.

SSE reconnects use exponential backoff. A periodic conditional poll can run alongside it as an additional safety net.

## Options

| Option | Default | Description |
|---|---|---|
| `url` | Required | Absolute HTTP or HTTPS ConfigHub delivery URL |
| `deliveryToken` | Required | Bearer token for the delivery endpoint |
| `fallbackPollInterval` | `null` | Optional conditional poll running alongside SSE |
| `sseReadIdleTimeout` | `null` | Reconnect if neither events nor keep-alive lines arrive in this interval |
| `handler` | `null` | Optional caller-owned `HttpMessageHandler`, useful for custom transport or tests |

Intervals must be greater than zero. The provider never disposes a supplied handler.

## Required Configuration

Mark the ConfigHub layer required when the application must not start without a successful initial snapshot:

```csharp
rule.For<AppSettings>()
    .FromConfigHub(deliveryUrl, deliveryToken)
    .Required()
```

This makes endpoint, authentication, and initial download failures fail startup through Cocoar.Configuration's normal required-rule behavior. Without `.Required()`, lower layers can continue to provide defaults according to the optional-rule policy.

## Dynamic Endpoints and Token Rotation

Endpoint and token can depend on configuration established by earlier rules:

```csharp
rule =>
[
    rule.For<DeploymentSettings>().FromEnvironment("DEPLOYMENT_"),
    rule.For<AppSettings>().FromConfigHub(accessor =>
    {
        var deployment = accessor.GetConfig<DeploymentSettings>()!;
        return new ConfigHubRuleOptions(
            $"https://config.example/api/config/{deployment.Application}",
            deployment.DeliveryToken,
            fallbackPollInterval: TimeSpan.FromMinutes(5));
    }),
]
```

When the endpoint or token changes, Cocoar.Configuration rebuilds the query and its live subscription. The delivery token is excluded from serialized rule identity and provider diagnostics; a SHA-256 fingerprint provides change identity without serializing the credential itself.

## Transport Customization

Pass a caller-owned handler for mutual TLS, proxy settings, or a custom `DelegatingHandler` chain:

```csharp
var handler = new HttpClientHandler();
handler.ClientCertificates.Add(clientCertificate);

rule.For<AppSettings>().FromConfigHub(
    deliveryUrl,
    deliveryToken,
    handler: handler)
```

A rule with a custom handler receives a dedicated provider instance so unrelated rules cannot accidentally share transport state.
