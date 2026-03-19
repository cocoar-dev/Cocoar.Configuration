# Database Provider

A native provider for loading configuration from relational databases — SQL Server, PostgreSQL, MySQL, and others via ADO.NET.

## Why

Many multi-tenant applications store per-tenant configuration in the database. Today, this requires a [custom provider](/guide/providers/custom). A native database provider would make this a one-liner.

## Planned API

```csharp
rule.For<TenantConfig>().FromDatabase(db =>
{
    db.ConnectionString = "Server=localhost;Database=myapp";
    db.Query = "SELECT config_json FROM tenant_config WHERE tenant_id = @tenantId";
    db.Parameters = new { tenantId = currentTenant.Id };
    db.PollInterval = TimeSpan.FromMinutes(1);
})
```

Or with config-aware connection strings:

```csharp
rule.For<TenantConfig>().FromDatabase(accessor =>
{
    var app = accessor.GetConfig<AppSettings>();
    return new DatabaseRuleOptions
    {
        ConnectionString = app.DatabaseConnectionString,
        Query = "SELECT config_json FROM tenant_config WHERE tenant_id = @id",
        Parameters = new { id = app.TenantId },
    };
})
```

## Planned Capabilities

- **Any ADO.NET provider** — SQL Server, PostgreSQL, MySQL, SQLite via standard `DbConnection`
- **JSON column support** — query returns a JSON string, merged into the config pipeline
- **Polling for changes** — configurable poll interval with hash-based change detection
- **Config-aware queries** — derive connection strings and query parameters from earlier rules
- **Change notifications** — optional SQL dependency / listen-notify support for push-based updates (PostgreSQL `LISTEN/NOTIFY`, SQL Server `SqlDependency`)

## Use Case: Multi-Tenant Configuration

The primary use case is ISVs that operate per-customer instances. Each customer has configuration stored in a shared or per-customer database:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),            // Base defaults
    rule.For<TenantSettings>().FromFile("tenant.json"),              // Which tenant is this?
    rule.For<TenantConfig>().FromDatabase(accessor =>                // Tenant-specific config
    {
        var tenant = accessor.GetConfig<TenantSettings>();
        return new DatabaseRuleOptions
        {
            ConnectionString = tenant.ConfigDbConnectionString,
            Query = "SELECT config FROM tenants WHERE id = @id",
            Parameters = new { id = tenant.TenantId },
        };
    }),
]
```

The tenant's database config overrides the file defaults — same merge semantics as any other provider.

## Status

Planned. This is the second-highest priority after cloud providers.
