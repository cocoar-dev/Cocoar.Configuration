# Building Custom Providers

If the built-in providers don't cover your data source, you can build your own by extending `ConfigurationProvider`.

## The Provider Contract

Every provider implements two methods:

```csharp
public abstract class ConfigurationProvider<TProviderConfiguration, TProviderQuery>
    where TProviderConfiguration : IProviderConfiguration
{
    protected TProviderConfiguration ProviderOptions { get; }

    // One-time fetch — called during recompute
    public abstract Task<byte[]> FetchConfigurationBytesAsync(
        TProviderQuery query, CancellationToken ct = default);

    // Change stream — called once, returns ongoing notifications
    public abstract IObservable<byte[]> ChangesAsBytes(TProviderQuery query);
}
```

- **`FetchConfigurationBytesAsync`** — returns the current configuration as UTF-8 JSON bytes
- **`ChangesAsBytes`** — returns an observable that emits new bytes whenever the source changes

Both methods receive a query object. The split between provider options and query options is important:

| Level | Scope | Example |
|---|---|---|
| `TProviderConfiguration` | Shared across rules | Base URL, directory path, connection string |
| `TProviderQuery` | Per-rule | Specific URL path, filename, key prefix |

## Example: Database Provider

A provider that reads configuration from a database table:

```csharp
// Provider options — shared, one instance per connection string
public record DatabaseProviderOptions(string ConnectionString) : IProviderConfiguration
{
    public string GenerateProviderKey() => ConnectionString;
}

// Query options — per-rule
public record DatabaseProviderQuery(string ConfigKey) : IProviderQuery;
```

```csharp
public class DatabaseConfigProvider
    : ConfigurationProvider<DatabaseProviderOptions, DatabaseProviderQuery>
{
    public DatabaseConfigProvider(DatabaseProviderOptions options) : base(options) { }

    public override async Task<byte[]> FetchConfigurationBytesAsync(
        DatabaseProviderQuery query, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(ProviderOptions.ConnectionString);
        await conn.OpenAsync(ct);

        var json = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT JsonValue FROM Configuration WHERE ConfigKey = @Key",
            new { Key = query.ConfigKey });

        return json is not null
            ? Encoding.UTF8.GetBytes(json)
            : "{}"u8.ToArray();
    }

    public override IObservable<byte[]> ChangesAsBytes(DatabaseProviderQuery query)
    {
        // No change detection — static until next recompute
        return ObservableHelpers.Never<byte[]>();

        // Or: implement SqlDependency, polling, etc.
    }
}
```

::: info Helper Utilities
`ObservableHelpers` and `DisposableHelpers` are lightweight utilities included in `Cocoar.Configuration` — no additional package needed. They provide `Never<T>()`, `Empty<T>()`, `Create<T>()` for observables and `Create(Action)` for disposables.
:::

## Provider Key and Instance Caching

`IProviderConfiguration.GenerateProviderKey()` controls provider reuse:

- **Return a string** — providers with the same key share one instance. Use this when the provider manages a shared resource (file watcher, database connection, HTTP client).
- **Return null** — each rule gets its own provider instance. Use this for providers with no shared state.

```csharp
public record DatabaseProviderOptions(string ConnectionString) : IProviderConfiguration
{
    // Same connection string = same provider instance
    public string GenerateProviderKey() => ConnectionString;
}
```

## Registering via Fluent API

Create an extension method on `TypedRuleBuilder<T>`:

```csharp
public static class DatabaseProviderRulesExtensions
{
    public static ProviderRuleBuilder<DatabaseConfigProvider,
        DatabaseProviderOptions, DatabaseProviderQuery>
        FromDatabase<T>(this TypedRuleBuilder<T> builder,
            string connectionString, string configKey)
    {
        return builder.FromProvider<T, DatabaseConfigProvider,
            DatabaseProviderOptions, DatabaseProviderQuery>(
            _ => new DatabaseProviderOptions(connectionString),
            _ => new DatabaseProviderQuery(configKey));
    }
}
```

Now it works like any built-in provider:

```csharp
rule.For<AppSettings>()
    .FromDatabase("Server=localhost;Database=Config", "AppSettings")
    .Required()
    .Named("Database Config")
```

## Change Detection

For reactive providers, return an `IObservable<byte[]>` that emits when the source changes:

```csharp
public override IObservable<byte[]> ChangesAsBytes(DatabaseProviderQuery query)
{
    return ObservableHelpers.Create<byte[]>(observer =>
    {
        var cts = new CancellationTokenSource();
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                var bytes = await FetchConfigurationBytesAsync(query, cts.Token);
                observer.OnNext(bytes);
            }
        }, cts.Token);

        return DisposableHelpers.Create(() =>
        {
            cts.Cancel();
            timer.Dispose();
        });
    });
}
```

The engine compares bytes by hash — if the content hasn't changed, no recompute is triggered. So it's safe to emit on every poll even when nothing changed.

## Complete Example

Here is a full custom provider as a single, self-contained block you can copy and adapt:

```csharp
// Complete DatabaseConfigProvider — copy and adapt

public record DatabaseProviderOptions(string ConnectionString) : IProviderConfiguration
{
    public string? GenerateProviderKey() => ConnectionString;
}

public record DatabaseProviderQuery(string ConfigKey) : IProviderQuery;

public class DatabaseConfigProvider
    : ConfigurationProvider<DatabaseProviderOptions, DatabaseProviderQuery>
{
    public DatabaseConfigProvider(DatabaseProviderOptions options) : base(options) { }

    public override async Task<byte[]> FetchConfigurationBytesAsync(
        DatabaseProviderQuery query, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(ProviderOptions.ConnectionString);
        await conn.OpenAsync(ct);

        var json = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT JsonValue FROM Configuration WHERE ConfigKey = @Key",
            new { Key = query.ConfigKey });

        return json is not null
            ? Encoding.UTF8.GetBytes(json)
            : "{}"u8.ToArray(); // Return empty JSON on failure, never null
    }

    public override IObservable<byte[]> ChangesAsBytes(DatabaseProviderQuery query)
    {
        return ObservableHelpers.Never<byte[]>(); // Or implement polling/notifications
    }
}

// Extension method for fluent API
public static class DatabaseProviderExtensions
{
    public static ProviderRuleBuilder<DatabaseConfigProvider,
        DatabaseProviderOptions, DatabaseProviderQuery>
        FromDatabase<T>(this TypedRuleBuilder<T> builder,
            string connectionString, string configKey) where T : class
    {
        return builder.FromProvider<T, DatabaseConfigProvider,
            DatabaseProviderOptions, DatabaseProviderQuery>(
            _ => new DatabaseProviderOptions(connectionString),
            _ => new DatabaseProviderQuery(configKey));
    }
}
```

Usage in rules:

```csharp
rule.For<AppSettings>()
    .FromDatabase("Server=localhost;Database=Config", "AppSettings")
    .Required()
    .Named("Database Config")
```

## Guidelines

- Always return `"{}"u8.ToArray()` on failure, never null — this keeps optional rules working
- Use `CancellationToken` throughout — providers are cancelled during shutdown
- Keep `FetchConfigurationBytesAsync` idempotent — it may be called multiple times
- Use `GenerateProviderKey()` to enable instance sharing when your provider holds expensive resources
- The change observable should not error — if it does, the subscription is lost

## Secrets in Custom Providers <Badge type="info" text="ADV" />

When your provider delivers secrets, emit them as encrypted envelopes. The engine stores envelopes as-is and decrypts on `Secret<T>.Open()`.

### X.509 Hybrid Envelope

The provider holds only the public certificate; the app holds the private key. The provider generates a random AES-256 DEK, encrypts content with AES-GCM, and wraps the DEK with RSA-OAEP-256:

```json
{
  "type": "cocoar.secret",
  "version": 1,
  "kid": "prod-secrets",
  "alg": "RSA-OAEP-AES256-GCM",
  "walg": "RSA-OAEP-256",
  "iv": "<base64 — AES-GCM nonce>",
  "ct": "<base64 — AES-GCM ciphertext>",
  "tag": "<base64 — AES-GCM auth tag>",
  "wk": "<base64 — wrapped DEK>"
}
```

The `kid` links to the certificate registered via `UseSecretsSetup()`. The provider never sees the private key.

### Rotation

Use a new `kid` for each key rotation. Apps register both old and new certificates during the overlap period via `WithAdditionalKeyId()`. New secrets use the new kid; old secrets remain decryptable until the old certificate is removed.
