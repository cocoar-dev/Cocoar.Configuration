---
description: AddCocoarConfigurationHealthCheck() for ASP.NET Core health checks, custom name and tags, HealthStatus to HealthCheckResult mapping, /health endpoint integration
---

# ASP.NET Core Health Checks

Cocoar.Configuration integrates with the standard ASP.NET Core health check system. One line registers it; the health endpoint then reflects configuration status alongside your other health checks.

::: info Package
Requires `Cocoar.Configuration.AspNetCore`.
:::

## Setup

```csharp
builder.Services
    .AddHealthChecks()
    .AddCocoarConfigurationHealthCheck();
```

That's it. The `/health` endpoint now includes Cocoar's configuration health.

## Customization

### Name and Tags

```csharp
builder.Services
    .AddHealthChecks()
    .AddCocoarConfigurationHealthCheck(
        name: "cocoar",
        tags: ["config", "startup"]);
```

Tags let you filter health checks in mapped endpoints:

```csharp
app.MapHealthChecks("/health/startup", new()
{
    Predicate = check => check.Tags.Contains("startup")
});
```

## Status Mapping

The health check maps Cocoar's `HealthStatus` to ASP.NET Core's `HealthCheckResult`:

| Cocoar Status | ASP.NET Core Result | Description |
|---|---|---|
| `Healthy` | `Healthy` | "All rules healthy" |
| `Degraded` | `Degraded` | Details (e.g., "1 optional rule(s) failed; expired feature flags detected") |
| `Unhealthy` | `Unhealthy` | Details (e.g., "1 required rule(s) failed") |
| `Unknown` | `Degraded` | "Health status unknown" |

`Unknown` maps to `Degraded` rather than `Unhealthy` because it typically means initialization is still in progress, not that something has failed.

## Example Response

A healthy response:

```json
{
  "status": "Healthy",
  "entries": {
    "cocoar-configuration": {
      "status": "Healthy",
      "description": "All rules healthy"
    }
  }
}
```

A degraded response (optional rule failed + expired flags):

```json
{
  "status": "Degraded",
  "entries": {
    "cocoar-configuration": {
      "status": "Degraded",
      "description": "1 optional rule(s) failed; expired feature flags detected"
    }
  }
}
```

## Combining with Other Health Checks

The Cocoar health check works alongside any other ASP.NET Core health checks:

```csharp
builder.Services
    .AddHealthChecks()
    .AddCocoarConfigurationHealthCheck()
    .AddDbContextCheck<AppDbContext>()
    .AddRedis(connectionString);
```

The overall health endpoint reports the worst status across all registered checks.
