# LocalStorage Provider

The LocalStorage provider reads and writes configuration to persistent storage. Unlike other providers, it's **writable from application code** — enabling runtime configuration changes via admin UIs, APIs, or background jobs.

```csharp
rule.For<AppSettings>().FromLocalStorage()
```

## How It Works

1. On startup, reads persisted bytes from the storage backend (default: JSON file on disk)
2. If no data exists yet, returns `{}` — the type is initialized with C# defaults
3. Application code writes new configuration via `ILocalStorage<T>` (injected through DI)
4. Write persists to storage, then signals the provider's change observable
5. The engine recomputes, and `IReactiveConfig<T>` emits the new value to all subscribers

```
ILocalStorage<T>.WriteAsync(value)
    → serialize to UTF-8 JSON bytes
    → persist to storage backend
    → signal change observable
    → engine recompute (debounced)
    → IReactiveConfig<T> fires
```

## Reading and Writing

Inject `ILocalStorage<T>` to read or write the stored configuration at runtime:

```csharp
// Read the raw stored value (not the merged pipeline result)
app.MapGet("/admin/settings", async (ILocalStorage<AppSettings> localStorage) =>
{
    var stored = await localStorage.ReadAsync();
    return stored is not null ? Results.Ok(stored) : Results.NotFound();
});

// Write — persists + triggers recompute
app.MapPut("/admin/settings", async (
    AppSettings settings,
    ILocalStorage<AppSettings> localStorage) =>
{
    await localStorage.WriteAsync(settings);
    return Results.Ok();
});
```

`ILocalStorage<T>` is registered as a **Singleton** — it's thread-safe and can be injected anywhere.

### Atomic Updates

Use `UpdateAsync` to modify individual properties without replacing the entire object. The read-mutate-write cycle runs under an exclusive lock — concurrent updates are serialized:

```csharp
// Toggle a single property — everything else is preserved
await localStorage.UpdateAsync(s => s.FeatureEnabled = true);

// Modify multiple properties atomically
await localStorage.UpdateAsync(s =>
{
    s.AppName = "NewName";
    s.MaxRetries = 5;
});
```

If nothing has been stored yet, `UpdateAsync` starts from a default-constructed `T`. Two concurrent `UpdateAsync` calls never lose each other's changes — the second one sees the first one's result.

### ReadAsync vs IReactiveConfig

| | `ILocalStorage<T>.ReadAsync()` | `IReactiveConfig<T>.CurrentValue` |
|---|---|---|
| Returns | Raw stored value | Merged pipeline result |
| Nothing stored | `null` | C# defaults (from `{}`) |
| Use case | Show admin what they saved | Show app what's effective |

::: info Package
`ILocalStorage<T>` is defined in `Cocoar.Configuration.Abstractions`, so library projects can depend on the interface without referencing the full configuration package.
:::

## Pipeline Position

LocalStorage is a normal rule in the pipeline. Position it to control priority:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),       // Defaults
    rule.For<AppSettings>().FromEnvironment("APP_"),            // Deployment overrides
    rule.For<AppSettings>().FromLocalStorage(),                 // Admin overrides (highest)
]
```

Later rules override earlier ones (last-write-wins). In this example, a value written via `ILocalStorage<AppSettings>` takes precedence over both the file and environment variables. But properties that weren't written still fall through to the earlier rules.

## Default Storage

By default, configuration is persisted as JSON files in:

```
{AppContext.BaseDirectory}/.cocoar/localStorage/
```

Each configuration type gets its own file, named by its full type name:

```
.cocoar/localStorage/
    MyApp.Settings.AppSettings.json
    MyApp.Settings.AuthSettings.json
```

Writes use an atomic temp-file-then-rename pattern to prevent partial reads.

## Custom Storage Backends <Badge type="info" text="ADV" />

The default file backend is just one implementation of `IStorageBackend`. You can replace it with any persistence layer.

### The Interface

```csharp
public interface IStorageBackend
{
    Task<byte[]?> ReadAsync(string key, CancellationToken ct = default);
    Task WriteAsync(string key, byte[] data, CancellationToken ct = default);
}
```

- **`key`** — the configuration type's full name (e.g., `"MyApp.Settings.AppSettings"`)
- **`ReadAsync`** — returns raw UTF-8 JSON bytes, or `null` if no data exists yet
- **`WriteAsync`** — persists raw UTF-8 JSON bytes atomically

### Example: Marten Backend

```csharp
public class MartenStorageBackend(IDocumentStore store) : IStorageBackend
{
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        await using var session = store.QuerySession();
        var doc = await session.LoadAsync<ConfigDocument>(key, ct);
        return doc?.JsonBytes;
    }

    public async Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();
        session.Store(new ConfigDocument { Id = key, JsonBytes = data });
        await session.SaveChangesAsync(ct);
    }
}

public class ConfigDocument
{
    public string Id { get; set; } = "";
    public byte[] JsonBytes { get; set; } = [];
}
```

### Example: SQLite Backend

```csharp
public class SqliteStorageBackend : IStorageBackend
{
    private readonly string _connectionString;

    public SqliteStorageBackend(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EnsureTable();
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM config WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as byte[];
    }

    public async Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config (key, data) VALUES (@key, @data)
            ON CONFLICT(key) DO UPDATE SET data = @data
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@data", data);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void EnsureTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS config (key TEXT PRIMARY KEY, data BLOB)";
        cmd.ExecuteNonQuery();
    }
}
```

### Using a Custom Backend

Pass your backend to `FromLocalStorage()`:

```csharp
var martenBackend = new MartenStorageBackend(documentStore);

rule => [
    rule.For<AppSettings>().FromLocalStorage(martenBackend),
    rule.For<AuthSettings>().FromLocalStorage(martenBackend),
]
```

Multiple types can share a single backend instance — they're distinguished by key.

### Config-Aware Backend (Factory Overload) <Badge type="info" text="ADV" />

When the storage backend depends on values from earlier rules (e.g., a connection string), use the factory overload. The factory receives two parameters:

- `accessor` — the current configuration state (values from earlier rules)
- `currentBackend` — the backend currently in use (`null` on first call)

```csharp
rule => [
    rule.For<InfraSettings>()
        .FromFile("infra.json")
        .Required(),

    rule.For<AppSettings>()
        .FromLocalStorage((accessor, currentBackend) =>
        {
            var infra = accessor.GetConfig<InfraSettings>();

            // Reuse existing backend if connection string hasn't changed
            if (currentBackend is MartenStorageBackend marten
                && marten.ConnectionString == infra.ConnectionString)
                return currentBackend;

            return new MartenStorageBackend(infra.ConnectionString);
        }),
]
```

The factory is called on **every recompute**. Returning `currentBackend` unchanged avoids creating a new instance — important for database backends where each instance may hold a connection pool. The store only swaps the backend when the returned reference is different from the current one.

#### Backend Swapping at Runtime

The factory can return a completely different backend type based on current configuration:

```csharp
rule.For<AppSettings>().FromLocalStorage((accessor, currentBackend) =>
{
    var infra = accessor.GetConfig<InfraSettings>();

    if (!string.IsNullOrEmpty(infra.DatabaseConnectionString))
    {
        // Reuse if same connection string
        if (currentBackend is MartenStorageBackend marten
            && marten.ConnectionString == infra.DatabaseConnectionString)
            return currentBackend;

        return new MartenStorageBackend(infra.DatabaseConnectionString);
    }

    // No DB configured — fall back to file
    if (currentBackend is FileStorageBackend)
        return currentBackend;

    return new FileStorageBackend();
})
```

When the earlier rule changes, the recompute triggers the factory with the new values. If the factory returns a different backend, it's swapped on the existing store — all `ILocalStorage<T>` references in DI remain valid and immediately use the new backend.

::: warning Data is not migrated
Swapping the backend does not move data from the old backend to the new one. After a swap, the new backend starts empty — reads return `{}` (C# defaults) until new data is written. This is consistent with how all providers behave: if the source is empty, you get defaults.
:::

## First Startup

When no data has been written yet:

- `ReadAsync` returns `null`
- The provider returns `{}`
- The configuration type is initialized with C# default values
- This is **not** an error — the rule is optional by default

The first `WriteAsync` call creates the persisted entry. From then on, the value is loaded on every startup.

## Common Patterns

### Admin settings endpoint

```csharp
// Read via IReactiveConfig (always current)
app.MapGet("/admin/settings", (IReactiveConfig<AppSettings> config) =>
    Results.Ok(config.CurrentValue));

// Write via ILocalStorage (persists + triggers recompute)
app.MapPut("/admin/settings", async (
    AppSettings settings,
    ILocalStorage<AppSettings> localStorage) =>
{
    await localStorage.WriteAsync(settings);
    return Results.Ok();
});
```

### File defaults + admin overrides

```csharp
rule => [
    rule.For<AppSettings>()
        .FromFile("appsettings.json")
        .Required(),

    rule.For<AppSettings>()
        .FromLocalStorage(),
]
```

The file provides the baseline. Admin changes override specific values without replacing the entire config.

### Multiple writable types

```csharp
rule => [
    rule.For<AuthSettings>().FromLocalStorage(),
    rule.For<BrandingSettings>().FromLocalStorage(),
    rule.For<NotificationSettings>().FromLocalStorage(),
]
```

Each type gets its own `ILocalStorage<T>` in DI and its own storage entry.

## Secrets

LocalStorage does **not** support `Secret<T>` properties. The standard secrets pipeline works because providers deliver pre-encrypted envelopes — decryption only happens at `Secret<T>.Open()` time, and plaintext is zeroed after use.

With LocalStorage, data arrives as plaintext JSON from application code (e.g., an admin UI). There is no point in the pipeline where encryption could happen — the value is already plaintext before `WriteAsync` is called, and it's stored as plaintext in the backend.

::: warning Do not store secrets via LocalStorage
`ILocalStorage<T>.WriteAsync()` persists values as plaintext JSON. There is no encryption, no envelope wrapping, and no zeroing. Sensitive values (API keys, tokens, passwords) should use [`UseSecretsSetup()`](/guide/secrets/overview) with file-based encrypted config instead.

If you need admin-editable sensitive settings, encrypt at the application layer before writing, or use a backend with built-in encryption (e.g., encrypted SQLite, Marten with column-level encryption, or a secrets manager).
:::
