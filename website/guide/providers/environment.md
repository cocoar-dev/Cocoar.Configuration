---
description: "FromEnvironment provider, case-insensitive prefix filtering, __ and : nesting to JSON, final-override pattern, dynamic per-tenant prefix"
---

# Environment Variables Provider

The environment variable provider reads environment variables and converts them to nested JSON.

```csharp
rule.For<AppSettings>().FromEnvironment("APP_")
```

## How It Works

1. Reads all environment variables at evaluation time
2. Filters by prefix (if specified)
3. Strips the prefix from matching keys
4. Converts flat key-value pairs to nested JSON

This provider is **static** — it reads environment variables once per recompute and does not watch for changes. Environment variables typically don't change during a process lifetime.

## Prefix Filtering

The prefix filters which variables are included. Matching is case-insensitive:

```csharp
// Only variables starting with "APP_"
rule.For<AppSettings>().FromEnvironment("APP_")

// All environment variables (no filtering)
rule.For<AppSettings>().FromEnvironment()
```

With prefix `APP_`, the variable `APP_MaxRetries=10` becomes `{ "MaxRetries": 10 }`.

## Nesting Convention

Use `__` (double underscore) or `:` to create nested JSON structures. A single `_` is treated as a literal character:

```shell
# Double underscore = nesting
APP_Database__Host=localhost
APP_Database__Port=5432

# Produces:
# { "Database": { "Host": "localhost", "Port": 5432 } }
```

```shell
# Single underscore = literal
APP_App_Name=MyApp

# Produces:
# { "App_Name": "MyApp" }
```

```shell
# Colon also works
APP_Database:Host=localhost

# Produces:
# { "Database": { "Host": "localhost" } }
```

## Common Pattern

Environment variables are typically the last rule, overriding everything else:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Required(),
    rule.For<AppSettings>().FromFile($"appsettings.{env}.json"),
    rule.For<AppSettings>().FromEnvironment("APP_"),  // Final override
]
```

This lets you override any config property via environment variables without touching files — useful for containers, CI/CD, and local development.

## Dynamic Prefix <Badge type="info" text="ADV" />

Use the factory overload to derive the prefix from earlier config:

```csharp
rule.For<TenantConfig>().FromEnvironment(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return new EnvironmentVariableRuleOptions($"TENANT_{tenant.TenantId}_");
})
```
