---
description: Declaring Secret<T> and ISecret<T> properties for strings, byte arrays and numbers, Open() leases and SecretLease<T> that zero decrypted bytes on dispose
---

# Secret\<T\> & Leases

`Secret<T>` is a property type that holds a value encrypted in memory. You access the decrypted value through a **lease** — a short-lived handle that zeros the decrypted bytes when disposed.

## Declaring Secrets

Use `Secret<T>` on properties that hold sensitive data:

```csharp
public class DatabaseConfig
{
    public required Secret<string> ConnectionString { get; init; }
    public Secret<string>? OptionalApiKey { get; init; }
}
```

`Secret<T>` works with any serializable type:

```csharp
// Strings (most common)
public required Secret<string> Password { get; init; }

// Byte arrays (for binary secrets like encryption keys)
public required Secret<byte[]> EncryptionKey { get; init; }

// Numbers
public Secret<int>? SecretPort { get; init; }
```

You can also use the interface `ISecret<T>` for properties if you prefer abstractions:

```csharp
public ISecret<string>? ApiKey { get; init; }
```

## Leases {#leases}

A lease provides temporary access to the decrypted value:

```csharp
using var lease = config.ConnectionString.Open();
var value = lease.Value;
// Use the value within this scope
// When the using block exits, decrypted bytes are zeroed
```

### Why Leases?

The lease pattern serves two purposes:

1. **Memory safety** — the decrypted `byte[]` is zeroed when the lease is disposed. The secret exists in plaintext memory only for the duration of the `using` block.

2. **Explicitness** — reading a secret is a deliberate action, not an accidental property access. This makes security-sensitive code paths visible in code review.

### SecretLease\<T\>

```csharp
public readonly struct SecretLease<T> : IDisposable
{
    public T Value { get; }
    public void Dispose();  // Zeros decrypted bytes
}
```

`SecretLease<T>` is a `readonly struct` — no heap allocation for the lease itself.

### Lease Lifecycle

```csharp
// 1. Open() decrypts the value
using var lease = secret.Open();

// 2. Value is available as plaintext
SendToDatabase(lease.Value);

// 3. Dispose() zeros the decrypted byte array
// (happens automatically at end of using block)
```

::: warning Strings Cannot Be Zeroed
`string` values in .NET are immutable — they cannot be overwritten in memory. For `Secret<string>`, the underlying byte array is zeroed, but the deserialized string remains in memory until garbage collected. For maximum security with binary secrets, use `Secret<byte[]>`.
:::

## Nullable Secrets <Badge type="info" text="ADV" />

A nullable `Secret<T>?` property means "this secret may not be present in the config":

```csharp
public class ApiConfig
{
    public required Secret<string> PrimaryKey { get; init; }   // Must exist
    public Secret<string>? FallbackKey { get; init; }          // May be absent
}
```

If `FallbackKey` is not in the JSON, the property is `null` — no lease to open.

## Encrypted vs Plaintext <Badge type="info" text="ADV" />

By default, `Secret<T>` expects an encrypted envelope in the JSON. Opening a plaintext secret throws `InvalidOperationException`:

```json
{ "Password": "plaintext-value" }
```

```csharp
config.Password.Open();  // Throws: plaintext not allowed
```

To allow plaintext (development/testing only):

```csharp
.UseSecretsSetup(secrets => secrets.AllowPlaintext())
```

See [Encryption Setup](/guide/secrets/encryption-setup) for configuring certificates.

## ISecret\<T\> Disposal <Badge type="info" text="ADV" />

`Secret<T>` implements `IDisposable`. When the configuration type is replaced by a recompute, the old instance's secrets are disposed — zeroing any remaining plaintext bytes held internally.

You don't need to dispose secrets manually. The configuration lifecycle handles it.
