# Encryption Setup

Secrets are encrypted with X.509 certificates using hybrid encryption: RSA-OAEP wraps an AES-256-GCM data encryption key.

## Single Certificate

The simplest setup — one certificate for all secrets:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [ /* ... */ ])
    .UseSecretsSetup(secrets => secrets
        .UseCertificateFromFile("certs/secrets.pfx")
        .WithKeyId("my-app")));
```

| Method | Description |
|---|---|
| `UseCertificateFromFile(path)` | Load a PFX or PEM certificate file |
| `WithKeyId(kid)` | Set the key identifier embedded in each encrypted secret |

The `kid` links encrypted envelopes to the certificate that can decrypt them. Each envelope records which `kid` was used to encrypt it.

### Supported Formats

| Format | Extensions | Notes |
|---|---|---|
| PKCS#12 | `.pfx`, `.p12` | Contains both public and private key |
| PEM | `.pem`, `.crt`, `.cer` | Requires matching `.key` file with same base name |

Certificates must be **password-less** at runtime and protected by file system permissions. See [Working with Certificates](/guide/certificates) for why and how.

### Path Resolution

Paths are resolved relative to `AppContext.BaseDirectory`:

```csharp
// Relative — from app base directory
.UseCertificateFromFile("certs/secrets.pfx")

// Absolute — used as-is
.UseCertificateFromFile("/etc/myapp/certs/secrets.pfx")
```

## Certificate Folder <Badge type="info" text="ADV" />

For multi-certificate setups and [rotation](/guide/secrets/security-model#rotation):

```csharp
.UseSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder("certs/", searchPattern: "*.pfx"))
```

The system monitors the folder and automatically discovers certificates:

| Parameter | Default | Description |
|---|---|---|
| `basePath` | (required) | Directory to scan for certificates |
| `searchPattern` | `"*"` | File filter — `"*.pfx"`, `"*.pem"`, or `"*"` for auto-discovery |
| `cacheDurationSeconds` | `30` | How long loaded certificates are cached |
| `certificateComparer` | null | Custom ordering for certificate priority |

### Multi-Tenant (Kid Subdirectories) <Badge type="info" text="ADV" />

Organize certificates by key ID using subdirectories:

```
certs/
├── tenant-a/
│   └── cert.pfx
├── tenant-b/
│   └── cert.pfx
└── shared/
    └── cert.pfx
```

Each subdirectory name becomes a `kid`. Secrets encrypted with `kid: "tenant-a"` are decrypted with the certificate in `certs/tenant-a/`.

## AllowPlaintext (Development)

For local development, skip encryption entirely:

```csharp
.UseSecretsSetup(secrets => secrets.AllowPlaintext())
```

With this enabled, `Secret<T>` properties deserialize from plain JSON values:

```json
{ "Password": "my-dev-password" }
```

A trace warning is emitted when `AllowPlaintext()` is active. Conditionally enable it:

```csharp
.UseSecretsSetup(secrets =>
{
    if (env.IsDevelopment())
        return secrets.AllowPlaintext();

    return secrets
        .UseCertificateFromFile("certs/prod.pfx")
        .WithKeyId("prod");
})
```

## The Encrypted Envelope <Badge type="info" text="ADV" />

When you encrypt a value, it produces this JSON structure:

```json
{
  "type": "cocoar.secret",
  "version": 1,
  "kid": "prod-secrets",
  "alg": "RSA-OAEP-AES256-GCM",
  "wk": "<base64 — AES key wrapped with RSA-OAEP>",
  "walg": "RSA-OAEP-256",
  "iv": "<base64 — AES-GCM nonce>",
  "ct": "<base64 — AES-GCM ciphertext>",
  "tag": "<base64 — AES-GCM auth tag>"
}
```

| Field | Purpose |
|---|---|
| `type` | Discriminator — always `"cocoar.secret"` |
| `version` | Format version (currently `1`) |
| `kid` | Key identifier — which certificate to use |
| `alg` | Overall algorithm |
| `wk` | Wrapped (encrypted) AES-256 data encryption key |
| `walg` | Key wrapping algorithm (RSA-OAEP-SHA256) |
| `iv` | 96-bit AES-GCM initialization vector |
| `ct` | Encrypted ciphertext |
| `tag` | 128-bit AES-GCM authentication tag |

The public key encrypts. The private key decrypts. You can safely commit the encrypted envelope to source control — without the private key, it's unreadable.

## How Decryption Works

1. Deserializer detects `"type": "cocoar.secret"` in the JSON
2. Reads the `kid` to find the matching certificate
3. Uses RSA-OAEP-SHA256 to unwrap the AES-256 data encryption key
4. Uses AES-256-GCM to decrypt the ciphertext (with authentication)
5. Returns the plaintext as `byte[]` inside a `Secret<T>` wrapper
6. The AES key is zeroed immediately after use

## Additional Key IDs <Badge type="info" text="ADV" />

Accept secrets encrypted with multiple certificates (during rotation):

```csharp
.UseCertificateFromFile("certs/prod-v2.pfx")
    .WithKeyId("prod-v2")
    .WithAdditionalKeyId("prod-v1")  // Still accept old certificate
```

See [Security Model](/guide/secrets/security-model#rotation) for the full rotation workflow.
