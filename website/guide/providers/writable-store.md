---
description: FromStore writable override layer, sparse leaf persistence, IWritableStore<T> SetAsync/ResetAsync/PatchAsync, reset vs explicit null, DescribeAsync provenance, secrets, IStoreBackend
---

# Writable Store Provider

The writable store is a **writable, application-controlled override layer**. Every other provider is an external source you read from — it is the one layer your application can write to at runtime.

Its purpose is **overridable defaults**: the normal sources (files, environment, …) supply defaults, and the application overrides *individual* values at runtime — from an admin UI, an API, or a background job — while everything it doesn't touch keeps inheriting from the lower layers.

```csharp
rules =>
[
    rules.For<SmtpSettings>().FromFile("appsettings.json"), // defaults
    rules.For<SmtpSettings>().FromStore(),           // app-controlled overrides (placed last → wins)
]
```

Position matters: place the writable-store rule **after** the rules whose values it should override.

## Sparse overrides

The writable store persists a **sparse** JSON object — only the leaves you explicitly set. Everything else is physically absent and therefore inherits from the lower layers through the normal byte-level merge.

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

Inject `IWritableStore<T>` (registered as a **Singleton**, thread-safe) to override values at runtime:

```csharp
public class SettingsController(IWritableStore<SmtpSettings> storage)
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

The selector must be a **simple member-access chain** (`x => x.Smtp.Port`). Indexers and method calls throw `NotSupportedException` (a type cast around the member chain is unwrapped and tolerated) — use the raw [overlay surface](#raw-overlay-surface) for dynamic paths.

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

## Batch writes — one save, one recompute

When more than one value changes together (a form save, an import), batch them with `PatchAsync`. Every mutation is applied under a **single** atomic read-merge-write — one write to the backend, one recompute — instead of one per property:

```csharp
await storage.PatchAsync(b => b
    .Set(x => x.Host, "smtp.example.com")
    .Set(x => x.Port, 587)
    .Set(x => x.UseSsl, true)
    .Reset(x => x.Timeout));            // mix sets and resets freely
```

A 20-field form save triggers **one** recompute and **one** backend write, not 20 — and subscribers of `IReactiveConfig<T>` never observe a half-applied state. For a database-backed `IStoreBackend` this also collapses 20 round-trips into a single transaction.

The single-value `SetAsync` / `SetSecretAsync` / `ResetAsync` are thin shorthands over `PatchAsync` for the one-property case.

### Write semantics — presence-based

There is no "magic null": what you call is exactly what happens.

| In the patch | Effect |
|---|---|
| `Set(x => x.Host, "v")` | sets the value |
| `Set(x => x.Host, null)` | sets an **explicit `null`** — only compiles where `null` is valid for the member (`string?`, `int?`, …) |
| *(Set not called)* | the property is **left untouched** |
| `Reset(x => x.Host)` | **removes** the override (restores inheritance) |

This is the only model that lets you set `null` explicitly *and* delete an override — they are different operations. Mapping external input (an HTTP body, an `Optional<T>` DTO's presence flags, …) onto these calls is **your** code's job; the library stays typed and never guesses.

### Secrets in a batch

Secret-typed members use `SetSecret` with a pre-encrypted [envelope](/guide/secrets/client-encryption):

```csharp
await storage.PatchAsync(b => b
    .Set(x => x.Port, 587)
    .SetSecret(x => x.ApiKey, envelope));
```

When gathering values is itself asynchronous (e.g. encrypting the envelope), use the async overload so you can `await` inside:

```csharp
await storage.PatchAsync(async b =>
    b.Set(x => x.Port, 587)
     .SetSecret(x => x.ApiKey, await EncryptAsync(apiKey)));
```

## Provenance for a management UI

`DescribeAsync()` returns, per key, the base value, the effective value, and whether it is currently overridden — everything a "default vs. override, with reset" UI needs:

```csharp
foreach (var entry in await storage.DescribeAsync())
{
    // entry.KeyPath, entry.BaseValue, entry.EffectiveValue, entry.IsSet
}
```

| KeyPath | BaseValue | EffectiveValue | IsSet |
|---|---|---|---|
| `Host` | `"smtp.default.com"` | `"smtp.default.com"` | `false` |
| `Port` | `25` | `587` | `true` |
| `UseSsl` | `false` | `true` | `true` |

`BaseValue` is the value computed from the layers **below** this overlay — i.e. what the key would be if the override were removed.

## Raw overlay surface

For dynamic or non-expressible paths, use `IWritableStoreOverlay<T>` (also resolvable directly from DI, or via `storage.Overlay`). Key paths are dotted; their segments correspond to the JSON property names:

```csharp
await storage.Overlay.SetAsync("Smtp.Port", JsonValue.Create(587));
await storage.Overlay.ResetAsync("Smtp.Port");
```

Key-path segments match the lower layers **case-insensitively** (the pipeline merges layers case-insensitively), so an override lands on the existing key regardless of casing — no need to mirror the exact casing of the base. Do **not** use the raw surface for secret paths.

## Arrays and secrets

- **Arrays are replaced wholesale.** `SetAsync(x => x.Hosts, list)` overrides the entire array — there is no element-level merge. Per-element selectors (`x => x.Hosts[2]`) are rejected.
- **Secrets need a pre-encrypted envelope.** A *plaintext* write of a `Secret<T>` / `ISecret<T>` member (via `Set` / `SetAsync`) throws `NotSupportedException` — it would persist the secret in the clear. To override a secret, use `SetSecret` (in a patch) or `SetSecretAsync` with a pre-encrypted [`SecretEnvelope<T>`](/guide/secrets/client-encryption). Resetting a secret override **is** allowed — it only removes the key and exposes no plaintext.

## Writing your own endpoints

There is no built-in REST surface — and that's deliberate. Writes are where *your* rules live (validation, normalization, authorization, audit logging, request shape), so the library gives you the injectable primitive and you own the endpoint. Inject `IWritableStore<T>` (or `IWritableStoreOverlay<T>`) anywhere and do your work *before* writing:

```csharp
app.MapPut("/admin/smtp/port", async (
    int port,
    IWritableStore<SmtpSettings> storage,
    ILogger<SmtpAdmin> log) =>
{
    if (port is < 1 or > 65535)                 // validate
        return Results.BadRequest("Port must be 1–65535.");

    log.LogInformation("Admin override SMTP.Port = {Port}", port); // audit
    await storage.SetAsync(x => x.Port, port);  // then persist (sparse) → recompute → reactive emit
    return Results.NoContent();
})
.RequireAuthorization("AdminPolicy");

// A full form save — many fields at once, one atomic write, one recompute:
app.MapPut("/admin/smtp", async (SmtpForm form, IWritableStore<SmtpSettings> storage) =>
{
    // validate/normalize `form` here, then map your DTO onto the typed patch:
    await storage.PatchAsync(b => b
        .Set(x => x.Host, form.Host)
        .Set(x => x.Port, form.Port)
        .Set(x => x.UseSsl, form.UseSsl));
    return Results.NoContent();
})
.RequireAuthorization("AdminPolicy");

// Expose the provenance view for a management UI:
app.MapGet("/admin/smtp", (IWritableStore<SmtpSettings> storage, CancellationToken ct) =>
    storage.DescribeAsync(ct));
```

For a generic admin UI that sets arbitrary keys, inject the raw `IWritableStoreOverlay<T>` and pass the dotted key path and a `JsonNode` yourself (your code is responsible for validating the path and value).

Both `IWritableStore<T>` and `IWritableStoreOverlay<T>` are registered by `AddCocoarConfiguration` as the **same** singleton instance, so either can be injected into controllers or minimal-API handlers.

## Store backends

By default, overrides are persisted as a JSON file under `{AppContext.BaseDirectory}/.cocoar/store/`, written atomically (temp-file-then-rename). Plug in your own store by implementing `IStoreBackend`:

```csharp
rules.For<SmtpSettings>().FromStore(new MyDatabaseBackend());
```

A config-aware overload receives the current configuration and backend, for backends whose connection depends on earlier rules (e.g. a connection string):

```csharp
rules.For<SmtpSettings>().FromStore((accessor, current) =>
    current ?? new DbBackend(accessor.GetConfig<DbSettings>()!.ConnectionString));
```

`ReadAsync` returns empty `{}` when nothing is stored (consistent with the [provider contract](/guide/providers/overview#the-provider-contract)), so an unwritten overlay is an invisible layer.

For a ready-made PostgreSQL backend — including **tenant-aware, database-per-tenant** storage where each tenant's configuration lives in its own database — see the [Marten Store](/guide/providers/marten-store) package.

## How it works

```
IWritableStore<T>.SetAsync(x => x.Port, 587)
    → resolve "Port" to a dotted key path
    → atomically read-merge-write the sparse overlay leaf to the backend
    → signal the provider's change observable
    → engine recompute (debounced) merges layers byte-for-byte, case-insensitively
    → IReactiveConfig<T> emits the new effective value
```

The read/merge path is identical to every other provider — the writable store only adds the write path. See the runnable [WritableStoreExample example](https://github.com/cocoar-dev/cocoar.configuration/tree/develop/src/Examples/WritableStoreExample) for an end-to-end walkthrough.
