# LocalStorage Provider

LocalStorage is a **writable, application-controlled override layer**. Every other provider is an external source you read from — LocalStorage is the one layer your application can write to at runtime.

Its purpose is **overridable defaults**: the normal sources (files, environment, …) supply defaults, and the application overrides *individual* values at runtime — from an admin UI, an API, or a background job — while everything it doesn't touch keeps inheriting from the lower layers.

```csharp
rules =>
[
    rules.For<SmtpSettings>().FromFile("appsettings.json"), // defaults
    rules.For<SmtpSettings>().FromLocalStorage(),           // app-controlled overrides (placed last → wins)
]
```

Position matters: place the LocalStorage rule **after** the rules whose values it should override.

## Sparse overrides

LocalStorage persists a **sparse** JSON object — only the leaves you explicitly set. Everything else is physically absent and therefore inherits from the lower layers through the normal byte-level merge.

Given the defaults `{ "Host": "smtp.default.com", "Port": 25, "UseSsl": false }`, after:

```csharp
await storage.SetAsync(x => x.Port, 587);
await storage.SetAsync(x => x.UseSsl, true);
```

the persisted overlay is just:

```json
{ "Port": 587, "UseSsl": true }
```

and the effective configuration is `Host=smtp.default.com` (inherited), `Port=587`, `UseSsl=true`.

This is the key difference from a "save the whole object" store: setting one value never freezes the others. If a default changes in the file later, every key you didn't override picks it up.

## Reading and writing

Inject `ILocalStorage<T>` (registered as a **Singleton**, thread-safe) to override values at runtime:

```csharp
public class SettingsController(ILocalStorage<SmtpSettings> storage)
{
    // Override a single value — only this leaf is persisted; a recompute fires
    // and IReactiveConfig<SmtpSettings> emits the new effective value.
    public Task SetPort(int port) => storage.SetAsync(x => x.Port, port);

    // Reset one override — the value falls back to the inherited default.
    public Task ResetPort() => storage.ResetAsync(x => x.Port);

    // Clear every override this layer holds.
    public Task ResetAll() => storage.ClearAsync();
}
```

The selector must be a **simple member-access chain** (`x => x.Smtp.Port`). Indexers, method calls, and casts throw `NotSupportedException` — use the raw [overlay surface](#raw-overlay-surface) for dynamic paths.

::: tip Writes are reactive
A write persists to storage, signals the provider, and triggers a (debounced) recompute. Subscribers of `IReactiveConfig<T>` receive the new merged value automatically.
:::

### Reset vs. explicit null

These are deliberately different operations:

| Operation | Overlay result | Effective value |
|---|---|---|
| `ResetAsync(x => x.Host)` | key removed | **inherits** the lower-layer value |
| `SetAsync(x => x.Host, null)` | `{ "Host": null }` | **overridden to `null`** (clobbers the base) |

### Overriding to a default-looking value

Because only touched keys are persisted, overriding to a value that happens to equal the C# default still counts as an override:

```csharp
await storage.SetAsync(x => x.Port, 0); // persists {"Port":0} → effective Port is 0, even if the base was 25
```

This is the headline correctness win: "an admin chose `0`" is distinct from "nobody set it."

### Reading the overlay

```csharp
SmtpSettings? overrides = await storage.ReadAsync();        // sparse partial T (unset members = C# defaults), null if empty
JsonNode?     raw       = await storage.Overlay.ReadOverlayAsync(); // the raw stored fragment, null if empty
```

`ReadAsync` returns only what the overlay holds — **not** the merged result. For the effective value use `IReactiveConfig<T>.CurrentValue` or `IConfigurationAccessor.GetConfig<T>()`.

## Provenance for a management UI

`DescribeAsync()` returns, per key, the base value, the effective value, and whether it is currently overridden — everything a "default vs. override, with reset" UI needs:

```csharp
foreach (var entry in await storage.DescribeAsync())
{
    // entry.KeyPath, entry.BaseValue, entry.EffectiveValue, entry.IsOverridden
}
```

| KeyPath | BaseValue | EffectiveValue | IsOverridden |
|---|---|---|---|
| `Host` | `"smtp.default.com"` | `"smtp.default.com"` | `false` |
| `Port` | `25` | `587` | `true` |
| `UseSsl` | `false` | `true` | `true` |

`BaseValue` is the value computed from the layers **below** this overlay — i.e. what the key would be if the override were removed.

## Raw overlay surface

For dynamic or non-expressible paths, use `ILocalStorageOverlay<T>` (also resolvable directly from DI, or via `storage.Overlay`). Key paths are dotted; their segments must match the persisted JSON property names:

```csharp
await storage.Overlay.SetAsync("Smtp.Port", JsonValue.Create(587));
await storage.Overlay.ResetAsync("Smtp.Port");
```

The typed facade aligns key casing to the lower layers for you; with the raw surface that responsibility is yours. Do **not** use the raw surface for secret paths.

## Arrays and secrets

- **Arrays are replaced wholesale.** `SetAsync(x => x.Hosts, list)` overrides the entire array — there is no element-level merge. Per-element selectors (`x => x.Hosts[2]`) are rejected.
- **Secrets are not overridable.** Members typed as `Secret<T>` / `ISecret<T>` throw `NotSupportedException` on the typed facade — an overlay write would replace the encrypted secret with a mask. Manage secrets via the Secrets CLI/provider.

## Writing your own endpoints

There is no built-in REST surface — and that's deliberate. Writes are where *your* rules live (validation, normalization, authorization, audit logging, request shape), so the library gives you the injectable primitive and you own the endpoint. Inject `ILocalStorage<T>` (or `ILocalStorageOverlay<T>`) anywhere and do your work *before* writing:

```csharp
app.MapPut("/admin/smtp/port", async (
    int port,
    ILocalStorage<SmtpSettings> storage,
    ILogger<SmtpAdmin> log) =>
{
    if (port is < 1 or > 65535)                 // validate
        return Results.BadRequest("Port must be 1–65535.");

    log.LogInformation("Admin override SMTP.Port = {Port}", port); // audit
    await storage.SetAsync(x => x.Port, port);  // then persist (sparse) → recompute → reactive emit
    return Results.NoContent();
})
.RequireAuthorization("AdminPolicy");

// Expose the provenance view for a management UI:
app.MapGet("/admin/smtp", (ILocalStorage<SmtpSettings> storage, CancellationToken ct) =>
    storage.DescribeAsync(ct));
```

For a generic admin UI that sets arbitrary keys, inject the raw `ILocalStorageOverlay<T>` and pass the dotted key path and a `JsonNode` yourself (your code is responsible for validating the path and value).

Both `ILocalStorage<T>` and `ILocalStorageOverlay<T>` are registered by `AddCocoarConfiguration` as the **same** singleton instance, so either can be injected into controllers or minimal-API handlers.

## Storage backends

By default, overrides are persisted as a JSON file under `{AppContext.BaseDirectory}/.cocoar/localStorage/`, written atomically (temp-file-then-rename). Plug in your own store by implementing `IStorageBackend`:

```csharp
rules.For<SmtpSettings>().FromLocalStorage(new MyDatabaseBackend());
```

A config-aware overload receives the current configuration and backend, for backends whose connection depends on earlier rules (e.g. a connection string):

```csharp
rules.For<SmtpSettings>().FromLocalStorage((accessor, current) =>
    current ?? new DbBackend(accessor.GetConfig<DbSettings>()!.ConnectionString));
```

`ReadAsync` returns empty `{}` when nothing is stored (consistent with the [provider contract](/guide/providers/overview#the-provider-contract)), so an unwritten overlay is an invisible layer.

## How it works

```
ILocalStorage<T>.SetAsync(x => x.Port, 587)
    → resolve "Port" to a dotted key path + align casing to the lower layers
    → atomically read-merge-write the sparse overlay leaf to the backend
    → signal the provider's change observable
    → engine recompute (debounced) merges layers byte-for-byte
    → IReactiveConfig<T> emits the new effective value
```

The read/merge path is identical to every other provider — LocalStorage only adds the write path. See the runnable [LocalStorageOverride example](https://github.com/) for an end-to-end walkthrough.
