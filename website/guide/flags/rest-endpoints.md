---
description: MapFeatureFlagEndpoints/MapEntitlementEndpoints GET/POST routes, custom path prefixes, RequireAuthorization and middleware chaining, error status codes, resolver-backed POST evaluation
---

# REST Evaluation Endpoints

The ASP.NET Core package provides REST endpoints for evaluating flags and entitlements over HTTP.

```csharp
app.MapFeatureFlagEndpoints();
app.MapEntitlementEndpoints();
```

::: info Package
Requires `Cocoar.Configuration.AspNetCore`.
:::

## Routes

Both methods generate routes for all registered flag/entitlement properties:

| Method | Route | Use Case |
|---|---|---|
| GET | `/{prefix}/{ClassName}/{PropertyName}` | No-context flags/entitlements |
| POST | `/{prefix}/{ClassName}/{PropertyName}` | Contextual flags/entitlements (request body = resolver input) |

Default prefixes: `/flags` for feature flags, `/entitlements` for entitlements.

### Examples

```
GET  /flags/AppFlags/DarkMode
→ { "value": true }

POST /flags/AppFlags/BetaFeature
     { "userId": "beta_123" }
→ { "value": true }

GET  /entitlements/PlanEntitlements/MaxUsers
→ { "value": 100 }

POST /entitlements/PlanEntitlements/RateLimit
     { "tenantId": "t_123" }
→ { "value": 10000 }
```

## Custom Path Prefix

```csharp
app.MapFeatureFlagEndpoints("/api/flags");
app.MapEntitlementEndpoints("/api/entitlements");
```

## Authorization

Both methods return a `RouteGroupBuilder` for chaining ASP.NET Core middleware:

```csharp
app.MapFeatureFlagEndpoints()
    .RequireAuthorization("AdminPolicy");

app.MapEntitlementEndpoints()
    .RequireAuthorization();
```

You can also add rate limiting, CORS, or any other endpoint middleware:

```csharp
app.MapFeatureFlagEndpoints()
    .RequireAuthorization()
    .RequireRateLimiting("fixed");
```

## Error Handling

| Scenario | Status | Response |
|---|---|---|
| Unknown key | 404 | Not found |
| Invalid request body | 400 | Bad request |
| Evaluation error | 500 | `{ "detail": "...", "title": "Flag evaluation failed", "statusCode": 500 }` |

## How It Works

- **GET endpoints** invoke the flag/entitlement delegate directly (no resolver needed)
- **POST endpoints** deserialize the request body to `TRequest`, pass it through the registered [Context Resolver](/guide/flags/context-resolvers), then invoke the delegate with the resolved context

The key format is `{ClassName}/{PropertyName}` — matching the class and property names from your code.
