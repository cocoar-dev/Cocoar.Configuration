# Security Model

This page explains the memory safety guarantees, encryption design, and certificate rotation behind the secrets system.

## Memory Safety

### The Lease Pattern

Decrypted values exist in plaintext memory only during the lease:

```csharp
using var lease = config.Password.Open();
// Plaintext exists in memory here
var value = lease.Value;
DoSomething(value);
// Dispose zeros the decrypted byte array
```

After `Dispose()`, the `byte[]` that held the decrypted data is overwritten with zeros via `Array.Clear()`.

### What Gets Zeroed

| Data | Zeroed? | How |
|---|---|---|
| Decrypted `byte[]` from envelope | Yes | `Array.Clear()` on lease dispose |
| AES data encryption key | Yes | `CryptographicOperations.ZeroMemory()` on stack, `Array.Clear()` on heap |
| RSA-unwrapped key material | Yes | Stack-allocated, zeroed after use |
| Deserialized `string` values | No | .NET strings are immutable |
| `Secret<T>` internal state on disposal | Yes | `Array.Clear()` on plaintext bytes, references nulled |

::: warning Strings in Memory
`Secret<string>` provides lease-based access, but the deserialized `string` value cannot be zeroed because .NET strings are immutable. The underlying `byte[]` is zeroed, but the string remains in memory until garbage collected.

For maximum memory safety with binary secrets (encryption keys, tokens), use `Secret<byte[]>`.
:::

### Stack Allocation

Temporary cryptographic keys use `stackalloc` to avoid heap allocation entirely:

```csharp
Span<byte> dek = stackalloc byte[32];
// ... use key ...
CryptographicOperations.ZeroMemory(dek);
```

Stack memory is automatically reclaimed when the method returns, providing an additional layer of protection.

## Encryption Algorithms

| Component | Algorithm | Key Size |
|---|---|---|
| Key wrapping | RSA-OAEP-SHA256 | Certificate key size (typically 2048+ bit) |
| Data encryption | AES-256-GCM | 256-bit |
| IV/Nonce | Random | 96-bit |
| Authentication tag | AES-GCM | 128-bit |

This is **hybrid encryption**: RSA encrypts a random AES key, AES encrypts the data. This combines RSA's key management with AES's efficiency for arbitrary-length data.

AES-GCM provides authenticated encryption — tampering with the ciphertext, IV, or wrapped key is detected and rejected.

## Provider Security

Providers handle raw bytes, never strings. This is by design:

- Providers return `byte[]` from `FetchConfigurationBytesAsync()`
- No string conversion happens until deserialization
- Providers never cache secret data — each fetch returns fresh bytes
- The file provider validates paths and rejects symlinks to prevent path traversal

## Certificate Protection

Certificates are protected by file system permissions, not passwords:

- Password-less PFX files simplify automated deployments
- File ACLs ensure only the application process can read the private key
- The certificate file contains both public and private keys — protect accordingly

## Certificate Rotation {#rotation}

### Single Certificate with Multiple Kids

Accept secrets encrypted with old and new certificates during a transition:

```csharp
.UseCertificateFromFile("certs/prod-v2.pfx")
    .WithKeyId("prod-v2")
    .WithAdditionalKeyId("prod-v1")
```

Secrets encrypted with `kid: "prod-v1"` still decrypt. New secrets are encrypted with the v2 certificate.

### Certificate Folder

For automated rotation, use a monitored folder:

```csharp
.UseCertificatesFromFolder("certs/", searchPattern: "*.pfx")
```

**Rotation workflow:**

1. Generate a new certificate: `cocoar-secrets generate-cert -o certs/prod-v2.pfx`
2. Drop it into the folder — auto-discovered by the file monitor
3. Encrypt new secrets with the new certificate's public key
4. Old secrets still decrypt with the old certificate
5. Eventually remove the old certificate when no active secrets use it

The system caches certificates (default 30s TTL) and invalidates the cache on file changes.

### Multi-Tenant Rotation

Organize by tenant in subdirectories:

```
certs/
├── tenant-a/
│   ├── cert-v1.pfx    # Previous
│   └── cert-v2.pfx    # Current
└── tenant-b/
    └── cert.pfx
```

Each subdirectory name is a `kid`. During rotation, both certificates coexist — the system tries them in order.

## What This Does NOT Protect Against

- **Process memory access** — if an attacker can read your process memory, they can see decrypted values during the lease window
- **String interning** — `Secret<string>` values may be interned by the runtime
- **Swap file** — decrypted memory could be paged to disk (use OS-level encrypted swap if this matters)
- **Logging** — if you log `lease.Value`, the secret is in your logs

Cleanup is best-effort because .NET is a managed runtime: the GC can move objects in memory (leaving copies), strings are immutable and may be interned, and there's no guarantee that `ZeroMemory` runs before a crash. Despite these constraints, the secrets system minimizes the plaintext window as aggressively as the runtime allows — pinned buffers, explicit zeroing, and deterministic disposal via leases. For secrets that never need to become strings, `Secret<byte[]>` provides the strongest guarantees.
