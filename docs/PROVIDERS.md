# Providers Overview

## Built-in Providers

* **Static Provider** – in-memory seeding with JSON strings or factory functions
* **File Provider** – JSON files with filesystem watching
* **Environment Provider** – environment variables with prefix filtering

## Extension Providers

* **HTTP Polling Provider** – remote polling with change detection
* **Microsoft Adapter** – integrate any `IConfigurationSource`

---

## Provider Features

| Provider          | Package   | Change Signal          | Notes                           |
| ----------------- | --------- | ---------------------- | ------------------------------- |
| Static            | Core      | ❌                      | JSON strings or factories       |
| File (JSON)       | Core      | ✅ Debounced FS watcher | Good base layer                 |
| Environment       | Core      | ❌ Snapshot only        | Prefix filter                   |
| HTTP Polling      | Extension | ✅ Payload diff         | Optional headers, interval      |
| Microsoft Adapter | Extension | Depends                | Wrap any `IConfigurationSource` |

---

## Static Provider Details

The Static Provider offers two approaches for in-memory configuration:

### JSON String Support
Perfect for testing, defaults, and simple configuration overrides:

```csharp
Rule.From.StaticJson("""
{
    "ApiUrl": "https://api.example.com",
    "Timeout": 30,
    "Features": {
        "EnableNewUI": true,
        "DebugMode": false
    }
}
""").For<AppConfig>()
```

### Factory Functions
Dynamic configuration generation using dependency injection:

```csharp
Rule.From.Static<DbConfig>(configManager => {
    var baseConfig = configManager.Get<BaseConfig>();
    return new DbConfig {
        ConnectionString = $"Server={baseConfig.DbServer};Database=MyApp",
        Timeout = baseConfig.IsProduction ? 60 : 30
    };
}).For<DbConfig>()
```

**Key Features:**
- No instance sharing - each rule gets isolated provider
- Strongly typed configuration objects
- Excellent for layered configuration (base + overrides)
- Zero I/O overhead

---

## Extensibility

Create custom providers by implementing the generic provider base and adding fluent entry points (e.g. `Rule.From.MyProvider()`).

See [Provider Development Guide](PROVIDER_DEV.md).
