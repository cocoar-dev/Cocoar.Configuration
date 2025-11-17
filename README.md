# Reactive, strongly-typed configuration layering for .NET

![Cocoar.Configuration](social-preview-small.png)
> Elevates configuration from hidden infrastructure to an observable, safety‑enforced subsystem you can trust under change and failure.

[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
[![Downloads](https://img.shields.io/nuget/dt/Cocoar.Configuration.svg)](https://www.nuget.org/packages/Cocoar.Configuration/)
---
> **📖 Articles & Deep Dives**
>
> • [Reactive, Strongly-Typed Configuration in .NET: Introducing Cocoar.Configuration v3.0 (Part 1)](https://dev.to/bwi/reactive-strongly-typed-configuration-in-net-introducing-cocoarconfiguration-v30-3gbn)  
>   Learn how v3.0 simplifies configuration management with zero-ceremony DI, atomic multi-config updates, and reactive patterns.  
>
> • [Config-Aware Rules in .NET — The Power Feature of Cocoar.Configuration (Part 2)](https://dev.to/bwi/config-aware-rules-in-net-the-power-feature-of-cocoarconfiguration-part-2-2ibk)  
>   Dive deeper into **atomic recompute**, **required vs optional rules**, and **config-aware conditional logic** for dynamic, tenant-aware setups.
---

### Shouldn't configuration be this easy?
```csharp
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Select("App"),
    rule.For<AppSettings>().FromEnvironment("APP_")
]);
```
It is. `AppSettings` is now injectable — and automatically updates when configs change.

---

## Install

```pwsh
dotnet add package Cocoar.Configuration
dotnet add package Cocoar.Configuration.AspNetCore

# Optional features:
dotnet add package Cocoar.Configuration.Secrets
dotnet add package Cocoar.Configuration.HttpPolling
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```

**Links:** [Cocoar.Configuration](https://www.nuget.org/packages/Cocoar.Configuration) · [AspNetCore](https://www.nuget.org/packages/Cocoar.Configuration.AspNetCore) · [Secrets](https://www.nuget.org/packages/Cocoar.Configuration.Secrets) · [HttpPolling](https://www.nuget.org/packages/Cocoar.Configuration.HttpPolling) · [MicrosoftAdapter](https://www.nuget.org/packages/Cocoar.Configuration.MicrosoftAdapter)

---

## Why Cocoar.Configuration?

Microsoft's `IConfiguration` works, but configuration deserves better. Here's what you get:

* **Zero ceremony** – Define a class, add a rule, inject it. No `Configure<T>()` calls, no `IOptions<T>` wrappers.
* **Atomic multi-config updates** – `IReactiveConfig<(T1, T2, T3)>` means multiple configs stay in sync. Never see inconsistent state.
* **Config-aware rules** – Rules can access earlier config to make decisions. Perfect for multi-tenant and dynamic scenarios.
* **Reactive by default** – Subscribe to changes automatically. No manual `IOptionsMonitor` wiring.
* **Explicit layering** – Rules execute in order, last write wins. No hidden merge logic.
* **Interface deserialization** – Support for interface-typed properties in config classes with explicit mapping.
* **Built-in health monitoring** – Track provider status and config changes with `IConfigurationHealthService`.
* **Memory-safe secrets** – `Secret<T>` with automatic zeroization and pre-encrypted envelope support.
* **✨ Compile-time validation** – Roslyn analyzers catch configuration errors while you code with red squiggles, automatic quick fixes, and CI/CD integration. Zero runtime cost. See [Analyzer Documentation](src/Cocoar.Configuration.Analyzers/README.md) for details on diagnostics (COCFG001-006).

**DI Lifetimes:** Concrete config types are registered as **Scoped** (stable snapshot per request), while `IReactiveConfig<T>` is **Singleton** (continuous live updates). These defaults can be customized via the `setup` parameter.

### Migration from IOptions

**Before (IConfiguration + IOptions):**
```csharp
// Startup configuration
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));

// Injection - requires wrapper
public class MyService(IOptions<AppSettings> options)
{
    var settings = options.Value; // Unwrap every time
}
```

**After (Cocoar.Configuration):**
```csharp
// Startup configuration
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Select("App")
]);

// Direct injection - no wrapper
public class MyService(AppSettings settings)
{
    // Just use it
}

// Or reactive
public class MyService(IReactiveConfig<AppSettings> config)
{
    config.Subscribe(newSettings => /* handle changes */);
}
```
---

## Quick Example

```csharp
var builder = WebApplication.CreateBuilder(args);

// Define your configuration rules
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Select("App"),
    rule.For<AppSettings>().FromEnvironment("APP_"),
    rule.For<DatabaseConfig>().FromFile("appsettings.json").Select("Database")
]);

var app = builder.Build();

// Direct injection - just use your POCO
app.MapGet("/api/status", (AppSettings settings) => 
    new { settings.Version, settings.FeatureFlags });

app.Run();
```

### For reactive scenarios (long-lived services):
```csharp
public class CacheWarmer : BackgroundService
{
    private readonly IReactiveConfig<AppSettings> _config;
    
    public CacheWarmer(IReactiveConfig<AppSettings> config)
    {
        _config = config;
        
        // Subscribe once - rebuild cache when settings change
        _config.Subscribe(newSettings =>
        {
            Console.WriteLine($"Config changed, rebuilding cache...");
            RebuildCache(newSettings);
        });
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
```

### SignalR hub that streams atomic multi-config updates
```csharp
public class ConfigHub : Hub
{
    private readonly IReactiveConfig<(AppSettings App, DatabaseConfig Db)> _configs;
    
    public ConfigHub(IReactiveConfig<(AppSettings App, DatabaseConfig Db)> configs)
    {
        _configs = configs;
        
        // Stream config changes to connected clients - always atomic
        _configs.Subscribe(async tuple =>
        {
            var (app, db) = tuple;
            await Clients.All.SendAsync("ConfigUpdated", new { app.Version, db.ConnectionString });
        });
    }
}
```

---

## What You Can Do

### Layer Configuration Sources
```csharp
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),           // Base
    rule.For<AppSettings>().FromFile("appsettings.Production.json"), // Environment
    rule.For<AppSettings>().FromEnvironment("APP_"),                 // Overrides
    rule.For<AppSettings>().FromCommandLine()                        // Final overrides (highest priority)
]);
// Rules execute in order - last write wins
```

**Environment variable mapping:**  
Hierarchical keys use `__` (double underscore):
```bash
APP_Database__Host=localhost
APP_Database__Port=5432
# Maps to: AppSettings.Database.Host and AppSettings.Database.Port
```

**Command-line argument mapping:**  
Hierarchical keys use `:` or `__`:
```bash
dotnet run --Database:Host=localhost --Database:Port=5432 --Verbose
# Maps to: AppSettings.Database.Host, AppSettings.Database.Port, and AppSettings.Verbose (true)
```

**Flexible switch prefixes for command-line arguments:**  
Use any prefix style (`--`, `-`, `/`, `@`, `#`, `%`) - even multiple at once:
```csharp
// Single custom prefix
rule.For<AppConfig>().FromCommandLine(["-"])     // Unix-style
rule.For<AppConfig>().FromCommandLine(["/"])     // Windows-style
rule.For<AppConfig>().FromCommandLine(["@"])     // Custom semantic style

// Multiple prefixes simultaneously
rule.For<AppConfig>().FromCommandLine(["--", "-", "/"])
```

```bash
# Mix different styles in the same command line
dotnet run --host=localhost -port=8080 /verbose

# Or use semantic prefixes for self-documenting CLIs
invoke.exe @target=server #issue=123 %env=prod
```

**Prefix filtering for command-line arguments:**  
Map arguments to specific configuration types:
```csharp
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppConfig>().FromCommandLine("app_"),
    rule.For<DatabaseConfig>().FromCommandLine("db_")
]);
```
```bash
dotnet run --app_host=localhost --db_connectionstring="Server=localhost"
# --app_host → AppConfig.Host (prefix stripped)
# --db_connectionstring → DatabaseConfig.ConnectionString (prefix stripped)
```

### Required vs Optional Rules
```csharp
// Required - fails fast if missing
rule.For<CoreSettings>().FromFile("required.json").Required()

// Optional - graceful degradation at runtime (default)
rule.For<OptionalSettings>().FromFile("optional.json")
```

### Conditional Rules
```csharp
rule.For<TenantSettings>().FromFile("tenant.json"),

rule.For<PremiumFeatures>().FromFile("premium.json")
    .When(accessor => accessor.GetRequiredConfig<TenantSettings>().IsPremium)
// Rules can access earlier config to make decisions
```

### Dynamic Configuration
```csharp
rule.For<TenantSettings>().FromFile("tenant.json"),

rule.For<ApiSettings>().FromHttpPolling(accessor =>
{
    var tenant = accessor.GetRequiredConfig<TenantSettings>();
    return new HttpPollingRuleOptions(
        $"https://{tenant.Region}.api.example.com/config",
        pollInterval: TimeSpan.FromMinutes(5)
    );
})
// Rules can derive behavior from earlier rules
```

### Interface Exposure
```csharp
builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromFile("appsettings.json")
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);

// Both AppSettings and IAppSettings are injectable
public class MyService(IAppSettings settings) { }
```

### Interface Deserialization

When your configuration classes have interface-typed properties, you need to map them to concrete types for JSON deserialization:

```csharp
// Configuration with interface properties
public class AppSettings
{
    public string AppName { get; set; }
    public ILoggingConfig Logging { get; set; }  // Interface property!
}

builder.Services.AddCocoarConfiguration(rule => [
    rule.For<AppSettings>().FromEnvironment()  // or FromFile, FromHttpPolling, etc.
], setup => [
    // Map interface to concrete type for deserialization
    setup.Interface<ILoggingConfig>().DeserializeTo<LoggingConfig>()
]);
```

**Why is this needed?** When loading configuration from JSON sources (files, environment variables, HTTP), properties typed as interfaces cannot be deserialized directly. This mapping tells the deserializer which concrete type to instantiate.

**Common scenarios:**
- Environment variables: `Logging__LogLevel__Default=Debug`
- Visual Studio hot reload injecting logging configuration
- Modular configuration with abstracted dependencies

**Supports nested interfaces:** If your interface properties contain other interface properties, just register all the mappings and they'll work at any depth.

---

## Providers

* **File** – JSON files with automatic change detection and reload
* **Environment Variables** – Prefix-based with hierarchical mapping (`__` for nesting)
* **Command-Line Arguments** – POSIX-style parsing with prefix support and nested configuration
* **HTTP Polling** – Remote config with polling; providers emit bytes and central dedup avoids churn ([Cocoar.Configuration.HttpPolling](https://www.nuget.org/packages/Cocoar.Configuration.HttpPolling))
* **Microsoft Adapter** – Bridge existing `IConfiguration` sources ([Cocoar.Configuration.MicrosoftAdapter](https://www.nuget.org/packages/Cocoar.Configuration.MicrosoftAdapter))
* **Static/Observable** – In-memory for testing and development

---

## Secrets Management

**Cocoar.Configuration.Secrets** provides memory-safe handling of sensitive configuration data — a unique capability in open-source configuration libraries:

* **`Secret<T>` type** – Automatic zeroization of sensitive data in memory
* **Pre-encrypted envelope support** – Secrets encrypted at rest in configuration files
* **X.509 certificate-based hybrid encryption** – RSA-OAEP + AES-GCM-256 for strong protection
* **On-demand decryption** – `Secret<T>.Open()` provides controlled exposure windows
* **CLI tools** – Certificate management and secret encryption workflows

```csharp
// Configuration with encrypted secrets
public class AppSettings
{
    public string AppName { get; set; }
    public Secret<DatabaseCredentials> DatabaseSecret { get; set; }
}

// Use secrets safely
using var exposed = settings.DatabaseSecret.Open();
var connectionString = BuildConnectionString(exposed.Value);
// Secret automatically zeroized when disposed
```

**Resources:**
* [Secrets Library Documentation](src/Cocoar.Configuration.Secrets/README.md) – API reference and patterns
* [CLI Tools Guide](src/Cocoar.Configuration.Secrets.Cli/README.md) – Certificate management and encryption
* [Basic Secrets Example](src/Examples/SecretsBasicExample) – Memory-safe secret handling
* [Certificate Secrets Example](src/Examples/SecretsCertificateExample) – Pre-encrypted secrets workflow

---

## Examples

Explore real-world scenarios in the [examples](src/Examples/) directory:

| Example | Description |
|---------|-------------|
| [BasicUsage](src/Examples/BasicUsage) | ASP.NET Core with file + environment layering |
| [FileLayering](src/Examples/FileLayering) | Multi-file layering (base/env/local) |
| [DynamicDependencies](src/Examples/DynamicDependencies) | Rules derived from earlier config |
| [ConditionalRulesExample](src/Examples/ConditionalRulesExample) | Config-aware conditional rules |
| [TupleReactiveExample](src/Examples/TupleReactiveExample) | Atomic multi-config snapshots |
| [HttpPollingExample](src/Examples/HttpPollingExample) | Remote HTTP config polling |
| [MicrosoftAdapterExample](src/Examples/MicrosoftAdapterExample) | Integrate existing `IConfiguration` sources |
| [SecretsBasicExample](src/Examples/SecretsBasicExample) | Memory-safe secret handling with `Secret<T>` |
| [SecretsCertificateExample](src/Examples/SecretsCertificateExample) | Pre-encrypted secrets with X.509 certificates |

[View all examples →](src/Examples/README.md)

---

## Documentation

| Topic | Link |
|-------|------|
| Reactive Configuration | [Reactive Config](docs/reactive-config.md) |
| Health Monitoring | [Health Monitoring](docs/health-monitoring.md) |
| Intelligent Certificate Caching | [Certificate Caching](src/Cocoar.Configuration.Secrets/intelligent-certificate-caching.md) |
| Provider Guidance | [Provider Guidance](docs/provider-guidance.md) |
| Migration from v2.x | [Migration Guide v2→v3](docs/migration-v2-to-v3.md) |
| Migration from v1.x | [Migration Guide v1→v2](docs/migration-v1-to-v2.md) |

---

## Testing

Over 200 automated tests covering:
* Configuration layering and rule execution
* Reactive updates and atomic snapshots
* Provider reliability and failover
* Concurrent access and race conditions
* Health monitoring and status tracking

See the [Testing Guide](src/tests/Cocoar.Configuration.Core.Tests/TESTING_GUIDE.md) for details.

---

## Contributing

Contributions welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

* **Semantic Versioning** – Breaking changes increment major version
* **Apache 2.0 License** – See [LICENSE](LICENSE) and [NOTICE](NOTICE)
* **Trademarks** – See [TRADEMARKS.md](TRADEMARKS.md)

---

## Security

To report security vulnerabilities, see [SECURITY.md](SECURITY.md).

**Best Practices:**
* Use **Cocoar.Configuration.Secrets** for sensitive configuration data (see Secrets Management section above)
* Enable TLS/HTTPS for remote configuration endpoints
* Avoid logging decrypted secrets or sensitive configuration values
* Use environment variables or Azure Key Vault (via Microsoft Adapter) for deployment-specific secrets
* Regularly rotate certificates used for secret encryption

**Runtime Security Posture:**

* Byte-only pipeline below the orchestrator: providers and RuleManager handle UTF-8 bytes, not strings
* Single parse point in the Configuration Orchestrator; no user-data strings are created below this layer
* Centralized dedup in RuleManager using SHA-256 over transformed bytes; avoids unnecessary recompute/IO
* Secure in-memory handling: transformed bytes are owned and zeroized on replace/dispose
* Behavior is unchanged for consumers in success paths; improved health signaling during sustained provider failures

---


