# Cocoar.Configuration.Secrets

Memory-safe secret handling for .NET with pre-encrypted configuration and X.509 certificate-based decryption.

## Overview

`Cocoar.Configuration.Secrets` provides secure secret management for configuration systems:

* **Memory-safe** - `Secret<T>` type with automatic memory zeroization
* **Pre-encrypted envelopes** - Secrets encrypted at rest by external systems (CI/CD, vaults, security teams)
* **On-demand decryption** - Secrets decrypted only when `.Open()` is called via certificate-based hybrid encryption
* **Zero plaintext exposure** - Secrets never appear in logs, console output, or memory dumps (shown as `***`)
* **Type-safe** - Works with any JSON-serializable type (`string`, `int`, objects, arrays)

## Installation

```bash
dotnet add package Cocoar.Configuration.Secrets
```

## Quick Start

### 1. Generate a Certificate

Use the [CLI tool](../Cocoar.Configuration.Secrets.Cli/README.md):

```bash
dotnet tool install --global Cocoar.Configuration.Secrets.Cli
cocoar-secrets generate-cert -o secrets.pfx
```

### 2. Configure the Secrets System

```csharp
using Cocoar.Configuration;
using Cocoar.Configuration.Secrets;

var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppConfig>().FromFile("config.json")
    ])
    .WithSecretsSetup(secrets => secrets
        .UseCertificateFromFile("secrets.pfx")  // Password-less certificate
        .WithKeyId("dev-secrets")));            // Matches kid in envelopes

var config = manager.GetConfig<AppConfig>();
```

### 3. Define Configuration with Secrets

```csharp
public class AppConfig
{
    public string AppName { get; set; }
    public DatabaseConfig Database { get; set; }
}

public class DatabaseConfig
{
    public string Host { get; set; }
    public Secret<string> ApiKey { get; set; }  // Memory-safe secret
    public Secret<ConnectionDetails> Connection { get; set; }  // Any type
}

public class ConnectionDetails
{
    public string Server { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
```

### 4. Use Pre-Encrypted Configuration

Your `config.json` contains **pre-encrypted envelopes** (created by external security tools or CLI):

```json
{
  "AppName": "MyApp",
  "Database": {
    "Host": "localhost",
    "ApiKey": {
      "_cocoar_secret": "v1",
      "kid": "dev-secrets",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "utf8",
      "createdAt": "2024-11-01T12:34:56Z",
      "iv": "base64-encoded-iv",
      "ct": "base64-encoded-ciphertext",
      "tag": "base64-encoded-auth-tag",
      "wk": "base64-encoded-wrapped-key"
    },
    "Connection": {
      "_cocoar_secret": "v1",
      "kid": "dev-secrets",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "json",
      "createdAt": "2024-11-01T12:35:00Z",
      "iv": "...",
      "ct": "...",
      "tag": "...",
      "wk": "..."
    }
  }
}
```

### 5. Access Secrets Safely

```csharp
// Open secret with automatic zeroization
using (var lease = config.Database.ApiKey.Open())
{
    string key = lease.Value;
    // WARNING: 'key' is now plain text - handle carefully!
    // The Secret<T> type protects values (shows as ***), but once you
    // extract .Value, you're responsible for not logging/exposing it

    // Use the secret (API calls, database connections, etc.)
    CallApiWithKey(key);
}
// Memory automatically zeroized after disposal

// Complex secrets work the same way
using (var lease = config.Database.Connection.Open())
{
    var details = lease.Value;
    var connection = $"Server={details.Server};User={details.Username};Password={details.Password}";
}
// All memory zeroized
```

## Encryption Process

**External systems encrypt secrets** before deployment:

```bash
# Using CLI tool
cocoar-secrets encrypt -f config.json \
    -p Database:ApiKey \
    -v "secret-api-key-value" \
    -c secrets.pfx \
    --kid dev-secrets

# Result: JSON file updated with encrypted envelope
```

**Encryption algorithm:** Hybrid RSA-OAEP + AES-256-GCM
1. Generate random AES-256 key
2. Encrypt secret with AES-256-GCM (authenticated encryption)
3. Wrap AES key with RSA-OAEP using certificate's public key
4. Store IV, ciphertext, auth tag, and wrapped key in envelope

## Certificate Management

### Single Certificate (Development)

```csharp
.WithSecretsSetup(secrets => secrets
    .UseCertificateFromFile("secrets.pfx")
    .WithKeyId("dev-secrets"))
```

### Multiple Certificates (Production with Rotation)

```csharp
// Folder structure: C:\certs\prod\{kid}\*.pfx
// Example: C:\certs\prod\api-keys\cert-v1.pfx
//          C:\certs\prod\api-keys\cert-v2.pfx

.WithSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder(@"C:\certs\prod",
        cacheDurationSeconds: 30))  // Cache for performance
```

**Certificate discovery:**
- Folders named by `kid` value (e.g., `api-keys`, `db-secrets`)
- All certificate files in kid folder are loaded (`.pfx`, `.p12`, `.cer`, `.crt`, `.key`)
- Decryption tries each certificate until one succeeds
- Certificates cached with TTL for performance

### Multi-Tier Security with Different Cache Strategies

```csharp
// Critical secrets - no cache (maximum security)
.WithSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder(@"C:\certs\pci",
        cacheDurationSeconds: 0))

// API keys - balanced 30-second cache
.WithSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder(@"C:\certs\api",
        cacheDurationSeconds: 30))

// Feature flags - 1-hour cache (performance)
.WithSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder(@"C:\certs\config",
        cacheDurationSeconds: 3600))
```

### Legacy Certificate Support

```csharp
.WithSecretsSetup(secrets => secrets
    .UseCertificateFromFile("current.pfx")
    .WithKeyId("prod-v2")
    .WithAdditionalKeyId("prod-v1"))  // Backward compatibility
```

## Certificate Formats

### Password-less Certificates (Recommended)

**Industry standard** (nginx, PostgreSQL, Kubernetes, Docker):

```bash
# Generate
cocoar-secrets generate-cert -o cert.pfx

# Protect with file permissions
chmod 600 cert.pfx  # Linux/macOS
# Windows: NTFS permissions (right-click → Properties → Security)

# Enable full-disk encryption
# BitLocker (Windows), LUKS (Linux), FileVault (macOS)
```

**Benefits:**
- No password management infrastructure needed
- No bootstrapping problem (passwords are secrets too)
- Same security level with file permissions + disk encryption

### PEM Format

Certificates can be in PEM format (`.crt` / `.cer` + `.key` files):

```bash
# Convert PFX → PEM
cocoar-secrets convert-cert -i cert.pfx -o cert.crt --format pem

# Use PEM in configuration
.WithSecretsSetup(secrets => secrets
    .UseCertificateFromFile("cert.crt", "cert.key"))  // Separate cert + key files
```

## Security Features

### Memory Safety

* **Automatic zeroization** - Memory cleared when `SecretLease` is disposed
* **No plaintext logging** - Secrets show as `***` in logs and ToString()
* **Controlled exposure** - Secrets only accessible via `.Open()` within `using` blocks
* **Type isolation** - `Secret<T>` cannot be accidentally passed as plain `T`

### Encryption Security

* **Hybrid encryption** - RSA-OAEP (key wrapping) + AES-256-GCM (data encryption)
* **Authenticated encryption** - GCM mode provides integrity protection
* **Random IVs** - New IV for every encryption operation
* **Key wrapping** - AES keys protected by RSA public key

### Runtime Security

* **Pre-encrypted only** - Plaintext secrets detected and rejected with clear errors
* **Certificate validation** - Certificates verified before use
* **Secure defaults** - Password-less certificates, file permissions, disk encryption

## Error Handling

### Plaintext Detection

```csharp
// If JSON contains plaintext instead of envelope:
// "ApiKey": "my-secret-value"

config.Database.ApiKey.Open();
// Throws: InvalidOperationException with message:
// "Secret is not initialized properly - expected encrypted envelope, got plaintext value"
```

### Missing Certificate

```csharp
// If certificate file not found or kid doesn't match:
// Throws: InvalidOperationException with clear error message
```

## Use Cases

### Multi-Environment Configuration

```
environments/
├── dev/
│   ├── config.json     # Pre-encrypted with dev-secrets kid
│   └── secrets.pfx
├── staging/
│   ├── config.json     # Pre-encrypted with staging-secrets kid
│   └── secrets.pfx
└── prod/
    ├── config.json     # Pre-encrypted with prod-secrets kid
    └── secrets.pfx
```

### Certificate Rotation

1. Generate new certificate: `cocoar-secrets generate-cert -o new.pfx`
2. Re-encrypt secrets with new certificate
3. Deploy both old and new certificates to production
4. Update configuration to use new kid
5. Remove old certificate after rollout complete

## Examples

* **[SecretsBasicExample](../Examples/SecretsBasicExample/)** - Basic secret handling with `Secret<T>`
* **[SecretsCertificateExample](../Examples/SecretsCertificateExample/)** - Advanced certificate management and rotation

## Advanced Topics

* **[Intelligent Certificate Caching](intelligent-certificate-caching.md)** - Performance optimization and security trade-offs

## CLI Tool

For certificate generation and secret encryption/decryption, use the CLI tool:

```bash
dotnet tool install --global Cocoar.Configuration.Secrets.Cli
```

See [CLI README](../Cocoar.Configuration.Secrets.Cli/README.md) for full documentation.

## License

Apache-2.0 - See [LICENSE](../../LICENSE) for details.
