# File Provider

The file provider reads JSON configuration files from disk and watches for changes.

```csharp
rule.For<AppSettings>().FromFile("appsettings.json")
```

## How It Works

1. Reads the file as UTF-8 bytes (strips BOM if present)
2. Starts a file system watcher on the directory
3. When the file changes on disk, triggers a recompute

The provider watches the **directory**, not individual files. Multiple rules reading from the same directory share one watcher.

## File Path Resolution

Paths are resolved relative to the application's base directory (`AppContext.BaseDirectory`):

```csharp
// Relative — resolved from app base directory
rule.For<AppSettings>().FromFile("appsettings.json")
rule.For<AppSettings>().FromFile("config/settings.json")

// Absolute — used as-is
rule.For<AppSettings>().FromFile("/etc/myapp/config.json")
```

## Debouncing

File saves often trigger multiple file system events in rapid succession. The engine applies configurable debouncing at two levels:

- **Recompute debounce** — the engine's global debounce (default 300ms) coalesces rapid changes across all providers
- **Per-file debounce** — available via the advanced API for file-specific throttling

## Security <Badge type="info" text="ADV" />

The file provider includes path traversal protection:

- Resolves the full path and validates it stays within the configured directory
- Rejects symlinks and reparse points to prevent symlink escape attacks
- Throws `UnauthorizedAccessException` on violations

## Advanced Options <Badge type="info" text="ADV" />

Use the factory overload for dynamic file paths or custom options:

```csharp
rule.For<AppSettings>().FromFile(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath(
        $"config/{tenant.TenantId}.json",
        debounceTime: TimeSpan.FromMilliseconds(500),
        pollingInterval: TimeSpan.FromSeconds(30));
})
```

| Option | Default | Description |
|---|---|---|
| `DebounceTime` | None (uses engine default) | Per-file debounce for change events |
| `PollingInterval` | 10 seconds | Fallback polling interval when file system events are unreliable |

## Missing Files

When the file doesn't exist:

- **Optional rules** (default) — the provider returns `{}`, contributing nothing. Values from earlier rules remain; if no earlier rule set a value, C# defaults apply
- **Required rules** — the recompute fails and rolls back (at startup, throws an exception)

This is by design. Use `.Required()` for files that must exist, and leave the default for optional overrides like `appsettings.local.json`.

## Common Patterns

### Base + environment overrides

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Required(),
    rule.For<AppSettings>().FromFile($"appsettings.{env}.json"),
]
```

### Per-tenant configuration

```csharp
rule.For<TenantConfig>().FromFile(accessor =>
{
    var tenant = accessor.GetConfig<TenantSettings>();
    return FileSourceRuleOptions.FromFilePath($"tenants/{tenant.TenantId}.json");
})
```

### Shared file, multiple types

```csharp
rule => [
    rule.For<AppConfig>().FromFile("appsettings.json").Select("App"),
    rule.For<DatabaseConfig>().FromFile("appsettings.json").Select("Database"),
]
```

Both rules share one file watcher because they read from the same directory.
