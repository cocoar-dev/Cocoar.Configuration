---
description: Cocoar.Configuration.ConfigHub provider — authenticated snapshots, ETag validation, SSE invalidation, reconnect reconciliation, polling fallback, dynamic endpoints, and reported dimensions with warning handling
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
| `dimensions` | none | Key/value pairs the client reports with every request (see [Reported Dimensions](#reported-dimensions)) |
| `warningMode` | `Warn` | `Warn` or `Fail` — how warnings about the reported dimensions are treated |
| `onWarnings` | `null` | Callback invoked when the warnings returned by ConfigHub change |

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

## Reported Dimensions

A ConfigHub product can let the client report the value of some dimensions — typically
where the process actually runs, such as the server — while others (for example the
customer) are defined centrally by the access. Report them as key/value pairs; not every
product needs the same ones:

```csharp
rule.For<AppSettings>().FromConfigHub(
    new ConfigHubRuleOptions(deliveryUrl, deliveryToken)
        .WithDimension("server", Environment.MachineName))
```

Values that depend on other configuration use the config-aware overload:

```csharp
rule.For<AppSettings>().FromConfigHub(accessor =>
    new ConfigHubRuleOptions(deliveryUrl, deliveryToken)
        .WithDimension("server", Environment.MachineName)
        .WithDimension("region", accessor.GetConfig<HostInfo>()!.Region))
```

The provider sends them with every snapshot request and SSE connection as
`ConfigHub-Dimension: region=eu, server=APPDEV01` (values URL-escaped). They are part of
the query identity: when a config-aware rule computes different values, the
subscription is rebuilt. An empty value removes the dimension.

### Warnings

ConfigHub still delivers when a reported dimension is missing, unknown or not allowed for
the access — without the affected layer — and says so in the `ConfigHub-Warning`
response header, for example `missing=server` or `unknown=server:APPTEST03`. The response
body remains the plain configuration.

What a warning means is the application's decision:

| Mode | Behavior |
|---|---|
| `ConfigHubWarningMode.Warn` (default) | Log the warnings (once per change), invoke `onWarnings`, keep the delivered configuration. |
| `ConfigHubWarningMode.Fail` | Treat the fetch as failed. With `.Required()` startup fails; an optional rule is reported as failed and configuration health becomes degraded. Later fetches with warnings are retried like other delivery errors. |

```csharp
rule.For<AppSettings>().FromConfigHub(
        new ConfigHubRuleOptions(deliveryUrl, deliveryToken)
            .WithDimension("server", Environment.MachineName)
            .WithWarnings(ConfigHubWarningMode.Fail))
    .Required()
```

## Schema and Secrets Registration

Besides fetching, a client can tell ConfigHub two things about itself, so ConfigHub renders a typed
form and can address [secrets](../secrets/overview.md) to it:

- **the JSON Schema** of the settings it binds, and
- **the public key** it opens secrets with.

Opt in once; both are derived from what the configuration already declares:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules =>
    [
        rules.For<StorageSettings>().FromConfigHub(hub).Select("Storage"),
        rules.For<MailSettings>().FromConfigHub(hub).Select("Mail"),
    ])
    .UseSecretsSetup(s => s.UseCertificateFromFile("certs/secrets.pfx"))
    .UseConfigHubRegistration(clientName: "MyApp"));
```

After the configuration is built, every active `FromConfigHub` rule contributes the schema of its
type at its `Select` path (`A:B` nests); rules whose `.When()` is false and other providers add
nothing. One schema is sent per ConfigHub endpoint, with the client name and version (default: the
entry assembly's version). When secrets are set up, the current public key is reported too. It runs
in the background; a refusal or an unreachable hub is logged, never thrown, and the server side is
idempotent, so it can run on every start.

`Secret<T>` properties appear in the schema as the schema of `T`, marked with
`"x-cocoar-secret": true` (`SecretJsonSchema`), so ConfigHub encrypts what is entered and stores only
the `cocoar.secret` envelope.

The key is never created implicitly — generate it once per instance with
`cocoar-secrets generate-cert -o certs/secrets.pfx` ([CLI](../secrets/cli.md)). Without a key, no key
is reported and secrets stay closed.

For full control, the same calls are available directly: `ConfigHubRegistration.BuildSchema`,
`RegisterSchemaAsync`, `ReportEncryptionKeyAsync`, and `ConfigManager.GetCurrentEncryptionKey()`.

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
