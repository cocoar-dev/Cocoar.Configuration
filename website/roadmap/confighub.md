# ConfigHub

The most common question: *"This is great, but how do I manage configuration across 100 instances without SSH-ing into each one?"*

**ConfigHub** is the answer — a management portal for Cocoar.Configuration deployments.

## The Problem

Cocoar.Configuration handles the runtime side: loading, merging, reacting, encrypting. But when you operate dozens or hundreds of instances, the operational questions are different:

- How do I push a config change to all production instances?
- Which instances have expired feature flags?
- When was the last certificate rotation, and which instances still use the old cert?
- A customer reports an issue — what's their current configuration state?

File-based config with manual deploys doesn't scale. You need a control plane.

## What ConfigHub Provides

### Configuration Management
Push config changes to instances or groups of instances without redeployment. Version history, diff view, rollback.

### Secrets Lifecycle
Certificate management, automated key rotation, encrypted secret distribution. No more managing N certificates for N instances manually — ConfigHub handles the lifecycle centrally and distributes keys to instances.

### Feature Flag Control
Enable/disable flags per instance, tenant, or environment. See which flags are active, which are expired, audit who changed what and when.

### Health Dashboard
Real-time health across all instances. Drill down from fleet overview to individual rule failures. Alert on degraded/unhealthy state before customers notice.

### Telemetry
Rich per-rule health snapshots, recompute timing, provider error rates, configuration drift detection — beyond what OpenTelemetry counters provide.

## Architecture

ConfigHub connects to your instances via the standard provider model. It's just another configuration source — the library doesn't know or care whether the bytes come from a file, HTTP endpoint, or ConfigHub:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),     // Local defaults
        rule.For<AppSettings>().FromConfigHub(),                   // Remote overrides from ConfigHub
    ]));
```

The `FromConfigHub()` provider uses the existing reactive pipeline — changes pushed from ConfigHub trigger the same recompute/merge/notify cycle as a file change. No special runtime behavior.

### Data Flow

```
ConfigHub Portal                    Your Instances
┌──────────────┐                   ┌──────────────────┐
│  Dashboard   │                   │  ConfigManager    │
│  Config UI   │ ── push/pull ──→  │  FromConfigHub()  │
│  Flag Control│                   │  reactive merge   │
│  Health View │ ←── telemetry ──  │  OpenTelemetry    │
└──────────────┘                   └──────────────────┘
```

Instances report health via standard OpenTelemetry. ConfigHub connects via OTLP — no proprietary agent or sidecar.

## Licensing

| | Library | ConfigHub |
|---|---|---|
| **License** | Apache-2.0 (free, forever) | Commercial (free tier available) |
| **What you get** | Full config, flags, entitlements, secrets, providers, analyzers | Management UI, push delivery, cert lifecycle, telemetry dashboards |
| **Dependency** | Standalone | Needs the library |
| **Required?** | N/A | No — the library works fully without it |

The library does **not** phone home, require a license key, or degrade without ConfigHub. It's a complete product on its own. ConfigHub is for teams that need the operational layer.

## Status

ConfigHub is in the design phase. Architecture, data model, and provider protocol are being defined. A private preview is planned after the cloud providers ship.

If you're interested in early access, watch the [GitHub repository](https://github.com/cocoar-dev/Cocoar.Configuration) for announcements.
