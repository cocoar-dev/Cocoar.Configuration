# Microsoft IConfiguration Adapter

The Microsoft adapter bridges an existing `IConfiguration` into Cocoar's rule system. Use it to migrate gradually or to reuse configuration sources that only exist as Microsoft providers.

```csharp
rule.For<AppSettings>().FromIConfiguration(builder.Configuration)
```

::: info Package
Requires the `Cocoar.Configuration.MicrosoftAdapter` package:
```shell
dotnet add package Cocoar.Configuration.MicrosoftAdapter
```
:::

## How It Works

1. Reads all key-value pairs from the given `IConfiguration`
2. Reconstructs nested JSON from the flat key structure (keys split by `:`)
3. Watches for changes via `IConfiguration.GetReloadToken()`

## Key Flattening

Microsoft's configuration system uses flat, colon-delimited keys. The adapter converts them back to nested JSON:

```
ConnectionStrings:Default      = "Server=localhost"
Logging:LogLevel:Default       = "Warning"
```

Becomes:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

## Section Filtering

Use `.Select()` to extract a subsection of the Microsoft configuration:

```csharp
rule.For<LoggingConfig>().FromIConfiguration(builder.Configuration).Select("Logging")
```

The adapter reads the full `IConfiguration` tree, and `.Select("Logging")` extracts only the `Logging` subtree before deserializing. This is the same `.Select()` method available on all Cocoar providers.

## Change Detection

The adapter watches for changes via `IConfiguration.GetReloadToken()`. When the underlying configuration signals a reload, the adapter re-reads all values and emits new bytes, triggering a recompute.

This means any `IConfiguration` backed by sources with `ReloadOnChange = true` works as expected.

## Common Patterns

### Gradual migration

Use the adapter for sources you haven't migrated yet, alongside native Cocoar providers:

```csharp
rule => [
    // Native Cocoar provider
    rule.For<AppSettings>().FromFile("appsettings.json"),

    // Legacy Microsoft configuration you're not ready to replace
    rule.For<AppSettings>().FromIConfiguration(builder.Configuration).Select("App"),
]
```

### Custom Microsoft providers

If a third-party library provides configuration through `IConfiguration`, pass it directly:

```csharp
rule.For<VaultConfig>().FromIConfiguration(vaultConfiguration)
```

## Limitations <Badge type="info" text="ADV" />

- **Array keys**: Microsoft uses `Key:0`, `Key:1` for arrays. The adapter converts these to JSON object properties (`"0": "value"`, `"1": "value"`), not JSON arrays. This matches Microsoft's own `IConfiguration` behavior.
- **Performance**: The adapter reads ALL key-value pairs from `IConfiguration` on each fetch. For very large configurations (thousands of keys), consider using `.Select()` to scope to the relevant section.
- **One-way bridge**: Changes flow FROM Microsoft configuration TO Cocoar. Cocoar does not write back to `IConfiguration`.
