---
description: FromFile JSON provider, directory file watcher, AppContext.BaseDirectory path resolution, debouncing, path-traversal protection, Kubernetes ConfigMap symlink support (followSymlinks), optional vs Required, dynamic paths
---

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
- **Rejects symlinks and reparse points by default** to prevent symlink-escape attacks
- Throws `UnauthorizedAccessException` on violations

To read symlinked files — e.g. [Kubernetes ConfigMap / Secret mounts](#kubernetes-configmap-secret-mounts) — opt in with `followSymlinks`. Even then, a symlink whose resolved target escapes the configured directory is still rejected.

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
| `FollowSymlinks` | `false` | Read symlinked files and detect atomic symlink-target swaps (see [Kubernetes ConfigMap / Secret mounts](#kubernetes-configmap-secret-mounts)) |

## Kubernetes ConfigMap / Secret mounts

A file mounted from a Kubernetes **ConfigMap** or **Secret** is a *symlink*: each key (e.g. `appsettings.json`) links through a sibling `..data` symlink to the real file in a timestamped directory. Kubernetes updates the volume by **atomically swapping the `..data` symlink** — it never rewrites the file you point at.

Because symlinks are rejected by default, opt in with `followSymlinks: true`:

```csharp
rule.For<AppSettings>().FromFile("/etc/config/appsettings.json", followSymlinks: true)
```

This does two things:

- **Reads** the symlinked file. The resolved final target must still resolve *within* the mount directory — an escaping symlink is rejected, so the path-traversal guarantees hold.
- **Hot-reloads** on the atomic `..data` swap, even though the watched file's name and timestamp are unchanged — the resolved symlink target is tracked for change detection.

`followSymlinks` is available on `FromFile`, [`FromYamlFile`](/guide/providers/yaml), and [`FromDotEnv`](/guide/providers/dotenv) (and as `FollowSymlinks` on `FileSourceProviderOptions`). It is **off by default**, so non-Kubernetes deployments keep the stricter symlink rejection.

::: tip Reload latency
The atomic `..data` swap does **not** trigger the instant OS file watcher — the file you point at is unchanged, only its symlink target moves. So a ConfigMap update is caught by the directory watcher's **periodic re-scan** (roughly every minute), plus kubelet's own propagation delay, rather than instantly. This interval is not currently tunable, and it's in line with how Kubernetes itself propagates ConfigMap changes (typically tens of seconds). Plan for **~1–2 minutes** end-to-end, not sub-second.
:::

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
