# Secrets API Reference

## Overview

The Cocoar Configuration Secrets API provides secure management of sensitive configuration data through **pre-encrypted envelopes**. Secrets must be encrypted by external systems (CI/CD pipelines, Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, etc.) before being loaded into the configuration system.

This document covers the complete secrets API available after calling `setup.Secrets()`.

**Architecture Principle:** Secrets arrive pre-encrypted and are only decrypted on-demand when `Secret<T>.Open()` is called.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Certificate-Based Decryption](#certificate-based-decryption)
   - [UseCertificateFromFile](#usecertificatefromfile)
   - [UseCertificatesFromFolder](#UseCertificatesFromFolder)
   - [UseSelfSignedCertificate](#useselfsignedcertificate)
3. [Custom Decryptors](#custom-decryptors)
4. [Secret Data Types](#secret-data-types)
5. [Complete API Reference](#complete-api-reference)
6. [Usage Examples](#usage-examples)
7. [Best Practices](#best-practices)

---

## Quick Start

### Basic Setup

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCertificateFromFile("certs/decrypt.pfx", "password")
            .WithKeyId("my-secrets")
            .Build()
    });

public class AppConfig
{
    public Secret<string> ApiKey { get; set; }
    public Secret<string> DatabasePassword { get; set; }
}

// Usage:
using (var lease = config.ApiKey.Open())
{
    var key = lease.Value;  // Access plaintext
    await client.AuthenticateAsync(key);
}
// Plaintext automatically zeroized after 'using' block
```

**Requirements:**
- Secrets in configuration must be pre-encrypted as envelopes with `_cocoar_secret` marker
- Decryption certificate registered with matching `kid` (key identifier)

**Example encrypted secret in JSON:**
```json
{
  "ApiKey": {
    "_cocoar_secret": "v1",
    "kid": "my-secrets",
    "alg": "RSA-OAEP-AES256-GCM",
    "type": "utf8",
    "createdAt": "2024-11-01T12:34:56Z",
    "iv": "AbCdEf...",
    "ct": "XyZ123...",
    "tag": "DeF456...",
    "wk": "GhI789..."
  }
}
```

---

## Certificate-Based Decryption

### UseCertificateFromFile

Load a certificate from a PFX file for decrypting pre-encrypted secrets.

```csharp
public CertificateSetupBuilder UseCertificateFromFile(string pfxPath, string? password = null)
```

**Parameters:**
- `pfxPath` - Absolute or relative path to PFX file
- `password` - Password for encrypted PFX files (optional if unprotected)

**Returns:** `CertificateSetupBuilder` for fluent configuration

**Use Case:** Decrypt secrets that were encrypted externally by CI/CD pipelines, security teams, or cloud services.

---

#### CertificateSetupBuilder Methods

##### WithKeyId

Sets the primary key identifier (Kid) for this certificate. This Kid identifies which secrets this certificate can decrypt.

```csharp
public CertificateSetupBuilder WithKeyId(string keyId)
```

**Default:** `"hybrid-encryption"`

**Example:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/prod.pfx", "password")
    .WithKeyId("production-secrets")  // Decrypts envelopes with: { "kid": "production-secrets", ... }
    .Build();
```

---

##### WithAdditionalKeyId

Adds additional key identifiers (aliases) for backward compatibility. The certificate can decrypt secrets encrypted with any of these Kids.

```csharp
public CertificateSetupBuilder WithAdditionalKeyId(string additionalKeyId)
```

**Use Case:** Migrate from old Kid values without re-encrypting all secrets.

**Example:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/unified.pfx", "password")
    .WithKeyId("production-v2")          // Primary Kid
    .WithAdditionalKeyId("production-v1") // Legacy alias (backward compat)
    .WithAdditionalKeyId("staging")       // Legacy alias (backward compat)
    .Build();
```

**Result:** Can decrypt secrets with Kid = `"production-v2"`, `"production-v1"`, or `"staging"`.

---

##### CreateSelfSignedIfNotExist

Allows automatic creation of a self-signed certificate if the PFX file doesn't exist. Useful for development environments.

```csharp
public CertificateSetupBuilder CreateSelfSignedIfNotExist(string? subjectName = null)
```

**Parameters:**
- `subjectName` - Subject name for the certificate (default: `"CN=Cocoar Configuration Dev Secrets"`)

**Example:**
```csharp
// Development environment - auto-create if missing
setup.Secrets()
    .UseCertificateFromFile("certs/dev.pfx", "DevPassword123")
    .CreateSelfSignedIfNotExist("CN=MyApp Dev Secrets")
    .WithKeyId("dev-secrets")
    .Build();

// Production environment - require existing file (omit CreateSelfSignedIfNotExist)
setup.Secrets()
    .UseCertificateFromFile("certs/prod.pfx", "ProdPassword456")
    .WithKeyId("prod-secrets")
    .Build();  // Throws if file doesn't exist ✅
```

---

##### Build

Completes the certificate configuration and returns to `SecretsSetup`.

```csharp
public SecretsSetup Build()
```

---

### UseCertificatesFromFolder

Configure certificate-based decryption from a folder containing multiple certificates. Enables **intelligent caching** and **automatic certificate rotation** for zero-downtime key management.

```csharp
public SecretsBuilder UseCertificatesFromFolder(
    string basePath,
    Func<CertificateContext, string[]>? passwordProvider = null,
    string searchPattern = "*.pfx",
    int cacheDurationSeconds = 30,
    IComparer<FileInfo>? certificateComparer = null)
```

**Parameters:**
- `basePath` - Path to folder containing certificate files (supports kid-based subdirectories: `basePath/{kid}/certificate.pfx`)
- `passwordProvider` - Function that receives `CertificateContext` and returns passwords to try. Context provides `Config` (configuration accessor), `FilePath`, and `Kid` (key identifier from folder structure)
- `searchPattern` - File pattern to match (default: `"*.pfx"`)
- `cacheDurationSeconds` - How long certificates stay cached in memory (default: 30 seconds, 0 = no cache)
- `certificateComparer` - Custom sorting for certificate priority when multiple match (default: newest file first by LastWriteTime)

**How It Works:**
- **Kid-based folder structure**: Certificates in `basePath/{kid}/` subfolders automatically associate with that key identifier
- **Global fallback**: Certificates directly in `basePath` are tried for all key identifiers
- **Intelligent Caching**: Uses two-level cache (envelope hash → cert path → loaded cert) for optimal performance
- **Automatic Discovery**: FileSystemWatcher monitors folder for certificate changes
- **Zero-Downtime Rotation**: Add new cert to folder, old secrets still decrypt with old cert
- **Configurable TTL**: Control how long certificates stay in memory via `cacheDurationSeconds`
- **Security**: Shorter cache duration = smaller attack surface for memory dumps

**When to Use:**
- Multiple certificates in production (rotation support)
- Need time-limited memory exposure for private keys
- Want automatic certificate discovery without restart
- Different certificates for different key identifiers (environments, services)

**Returns:** `SecretsBuilder` (no fluent builder - configuration is complete)

**Benefits over `UseCertificateFromFile`:**
- ✅ Zero-downtime certificate rotation (add new cert, old secrets still work)
- ✅ Reduced memory footprint (certs cached with TTL instead of forever)
- ✅ Automatic discovery (no restart needed when certs added)
- ✅ Intelligent two-level caching (envelope hash + cert cache)
- ✅ Kid-based folder structure for multi-environment deployments

**CertificateContext Structure:**
```csharp
public sealed class CertificateContext
{
    public required IConfigurationAccessor Config { get; init; }  // Full configuration access
    public required string FilePath { get; init; }                // Path to certificate file
    public string? Kid { get; init; }                             // Key identifier (from folder name or null)
}
```

**Example - Simple folder with fixed password:**
```csharp
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs", ctx => new[] { "MyPassword123" });
```

**Example - Kid-based folder structure with configuration-driven passwords:**
```csharp
// Folder structure:
//   C:\certs\production\cert.pfx
//   C:\certs\staging\cert.pfx
//   C:\certs\fallback.pfx

setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs", passwordProvider: ctx =>
    {
        // Try environment-specific password from config
        if (ctx.Kid != null)
        {
            var envPassword = ctx.Config.GetValue<string?>($"Certificates:{ctx.Kid}:Password");
            if (envPassword != null)
                return new[] { envPassword };
        }
        
        // Fallback to default password
        return new[] { ctx.Config.GetValue<string>("Certificates:DefaultPassword") };
    });
```

**Example - Custom cache duration and certificate priority:**
```csharp
// No caching for maximum security (load fresh every time)
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\critical", 
        passwordProvider: ctx => new[] { "SecurePassword" },
        cacheDurationSeconds: 0);

// Extended caching for non-sensitive data
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\features",
        passwordProvider: ctx => new[] { "FeaturePassword" },
        cacheDurationSeconds: 3600);  // 1 hour
```

**Security vs Performance Trade-off (cacheDurationSeconds):**

| Duration | Security Level | Use Case | Performance |
|----------|---------------|----------|-------------|
| **0s** | Maximum | PCI-DSS, HIPAA, passwords | File I/O every decrypt |
| **5-30s** | High | API keys, user credentials | 100-1000x faster |
| **60-300s** | Medium | Service credentials | Minimal I/O |
| **3600s+** | Low | Feature flags, non-sensitive | Maximum performance |

**See also:** [Intelligent Certificate Caching](intelligent-certificate-caching.md) for detailed architecture

---

### UseSelfSignedCertificate

Configures X.509 certificate-based hybrid encryption using a self-signed certificate stored in a PFX file. If the file doesn't exist, a new self-signed certificate will be created and saved.

```csharp
public SecretsBuilder UseSelfSignedCertificate(
    string pfxPath, 
    string password, 
    string? keyId = null,
    string? subjectName = null)
```

**Parameters:**
- `pfxPath` - Path to the PFX file (created if missing)
- `password` - Password for the PFX file
- `keyId` - Optional key identifier (default: `"hybrid-encryption"`)
- `subjectName` - Subject name for the certificate (default: `"CN=Cocoar Configuration Dev Secrets"`)

**Use Case:** Simple development setup with persistent encryption across app restarts.

**Example:**
```csharp
setup.Secrets()
    .UseSelfSignedCertificate("secrets.pfx", "DevPassword123", keyId: "dev-cert")
```

**Behavior:**
- First run: Creates `secrets.pfx` with new self-signed certificate
- Subsequent runs: Loads existing certificate from `secrets.pfx`
- Secrets encrypted on first run can be decrypted on subsequent runs

---

## Custom Decryptors

For advanced scenarios like Azure Key Vault, AWS KMS, or Hardware Security Modules.

### UseCustomDecryptor

Registers a custom decryptor for handling pre-encrypted secrets from external sources.

```csharp
public SecretsSetup UseCustomDecryptor<TEnvelope>(ISecretDecryptor<TEnvelope> decryptor)
    where TEnvelope : IEnvelope
```

**Parameters:**
- `decryptor` - Custom decryptor implementing `ISecretDecryptor<TEnvelope>`

**Use Case:** Decrypt secrets encrypted by external systems (CI/CD, security teams, cloud HSMs) where encryption happens outside your application.

**Example:**
```csharp
public class AzureKeyVaultDecryptor : ISecretDecryptor<AkvEnvelope>
{
    private readonly KeyClient _keyClient;

    public string Kid => "akv-prod-v1";

    public AzureKeyVaultDecryptor(string keyVaultUrl)
    {
        _keyClient = new KeyClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
    }

    public byte[] Unprotect(AkvEnvelope envelope)
    {
        // Decrypt with Azure Key Vault
        var decryptResult = _keyClient.Decrypt(EncryptionAlgorithm.RsaOaep, envelope.Ciphertext);
        return decryptResult.Plaintext;
    }

    public AkvEnvelope DeserializeEnvelope(string json) => 
        JsonSerializer.Deserialize<AkvEnvelope>(json)!;
}

// Usage:
var akvDecryptor = new AzureKeyVaultDecryptor("https://mykeyvault.vault.azure.net/");

var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCustomDecryptor(akvDecryptor)
    });
```

---

## Secret Data Types

The Secrets API provides specialized types for handling sensitive data with automatic memory zeroization.

### Secret<T>

A handle for secret values of any JSON-serializable type. The plaintext is only exposed via `Open()` and zeroized on dispose.

**Properties:**
- `ToString()` → Always returns `"***"` (never leaks secrets)

**Methods:**
- `Open()` → Returns `SecretLease<T>` with short-lived access to plaintext

**Example:**
```csharp
public class AppConfig
{
    public Secret<string> ApiKey { get; set; }
    public Secret<DatabaseConfig> DbConfig { get; set; }
}

// Usage:
using (var lease = config.ApiKey.Open())
{
    var key = lease.Value;  // Access plaintext
    var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-API-Key", key);
}
// Plaintext automatically zeroized after 'using' block
```

---

### SecretLease<T>

A short-lived lease exposing secret material. Dispose to zeroize when possible.

**Properties:**
- `Value` → The decrypted secret value

**Methods:**
- `Dispose()` → Zeroizes memory (best-effort for string/byte[])

**Usage pattern:**
```csharp
using (var lease = secret.Open())
{
    // Use lease.Value
    DoSomethingWith(lease.Value);
}
// Memory zeroized automatically
```

---

### SecretEnvelope

Internal structure representing an encrypted secret. Contains metadata and encrypted data.

**Properties:**
- `_cocoar_secret` - Version marker (required, must be `"v1"`)
- `kid` - Key identifier (required, determines which decryptor to use)
- `alg` - Algorithm identifier (optional, for observability)
- `type` - Data type: `"utf8"` (string), `"bytes"`, or `"json"`
- `createdAt` - Timestamp when envelope was created
- Encryption data: `iv`, `ct` (ciphertext), `tag`, `wk` (wrapped key), etc.

**Example envelope in JSON:**
```json
{
  "_cocoar_secret": "v1",
  "kid": "production-secrets",
  "alg": "RSA-OAEP-AES256-GCM",
  "type": "utf8",
  "createdAt": "2024-11-01T12:34:56Z",
  "iv": "AbCdEf...",
  "ct": "XyZ123...",
  "tag": "DeF456...",
  "wk": "GhI789..."
}
```

---

## Complete API Reference

### SecretsBuilder Extension Methods

Extension methods for configuring secrets on `SecretsBuilder`.

```csharp
public static class SecretsHybridExtensions
{
    // Single certificate with fluent builder
    public static CertificateSetupBuilder UseCertificateFromFile(
        this SecretsBuilder builder,
        string pfxPath, 
        string? password = null);

    // Folder with multiple certificates (direct configuration, no builder)
    public static SecretsBuilder UseCertificatesFromFolder(
        this SecretsBuilder builder,
        string basePath,
        Func<CertificateContext, string[]>? passwordProvider = null,
        string searchPattern = "*.pfx",
        int cacheDurationSeconds = 30,
        IComparer<FileInfo>? certificateComparer = null);

    // Self-signed certificate (creates if missing)
    public static SecretsBuilder UseSelfSignedCertificate(
        this SecretsBuilder builder,
        string pfxPath, 
        string password, 
        string? keyId = null,
        string? subjectName = null);
}
```

---

### CertificateSetupBuilder

Fluent builder for single certificate configuration returned by `UseCertificateFromFile`.

```csharp
public sealed class CertificateSetupBuilder
{
    public CertificateSetupBuilder WithKeyId(string keyId);
    public CertificateSetupBuilder WithAdditionalKeyId(string additionalKeyId);
    public CertificateSetupBuilder CreateSelfSignedIfNotExist(string? subjectName = null);
    public CertificateSetupBuilder WatchForChanges();
    public SecretsBuilder Build();
}
```

---

### ISecretDecryptor<TEnvelope>

Interface for custom decryptors.

```csharp
public interface ISecretDecryptor<TEnvelope> where TEnvelope : IEnvelope
{
    string Kid { get; }
    byte[] Unprotect(TEnvelope envelope);
    TEnvelope DeserializeEnvelope(string json);
}
```

**Purpose:** Decrypt pre-encrypted secrets from external sources (CI/CD, Azure Key Vault, AWS Secrets Manager, etc.)

---

## Usage Examples

### Example 1: Production with Pre-Encrypted Secrets

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCertificateFromFile("certs/prod.pfx", Environment.GetEnvironmentVariable("CERT_PASSWORD"))
            .WithKeyId("production-secrets")
            .Build()
    });

public class ProdConfig
{
    public Secret<string> DatabasePassword { get; set; }
    public Secret<string> ApiKey { get; set; }
}
```

---

### Example 2: Multi-Environment with Certificate Rotation

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        // Production secrets with automatic rotation
        // Uses kid-based folder structure: C:\certs\prod\production-secrets\cert.pfx
        setup.Secrets()
            .UseCertificatesFromFolder(@"C:\certs\prod", 
                passwordProvider: ctx => new[] { Environment.GetEnvironmentVariable("CERT_PASSWORD") ?? "" },
                cacheDurationSeconds: 30),

        // Legacy secrets (backward compatibility)
        setup.Secrets()
            .UseCertificateFromFile("certs/legacy.pfx", "password")
            .WithKeyId("hybrid-encryption")
            .WithAdditionalKeyId("old-prod-v1")
            .Build()
    });
```

---

### Example 3: Multi-Tier Security

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        // Tier 1: Critical (PCI-DSS) - No cache, load fresh every time
        // Folder structure: C:\certs\pci\pci-data\*.pfx
        setup.Secrets()
            .UseCertificatesFromFolder(@"C:\certs\pci", 
                passwordProvider: ctx => new[] { "password" },
                cacheDurationSeconds: 0),  // Maximum security

        // Tier 2: High (API keys) - Balanced 30-second cache
        // Folder structure: C:\certs\api\api-keys\*.pfx
        setup.Secrets()
            .UseCertificatesFromFolder(@"C:\certs\api", 
                passwordProvider: ctx => new[] { "password" },
                cacheDurationSeconds: 30),

        // Tier 3: Medium (feature flags) - 1-hour cache for performance
        // Folder structure: C:\certs\config\feature-flags\*.pfx
        setup.Secrets()
            .UseCertificatesFromFolder(@"C:\certs\config", 
                passwordProvider: ctx => new[] { "password" },
                cacheDurationSeconds: 3600)
    });
```

---

### Example 4: Azure Key Vault Integration

```csharp
public class AzureKeyVaultDecryptor : ISecretDecryptor<AkvEnvelope>
{
    private readonly KeyClient _keyClient;
    public string Kid => "akv-prod-v1";

    public AzureKeyVaultDecryptor(string keyVaultUrl)
    {
        _keyClient = new KeyClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
    }

    public byte[] Unprotect(AkvEnvelope envelope)
    {
        var result = _keyClient.Decrypt(EncryptionAlgorithm.RsaOaep, envelope.Ciphertext);
        return result.Plaintext;
    }

    public AkvEnvelope DeserializeEnvelope(string json) => 
        JsonSerializer.Deserialize<AkvEnvelope>(json)!;
}

// Usage:
var akvDecryptor = new AzureKeyVaultDecryptor("https://mykeyvault.vault.azure.net/");

var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCustomDecryptor(akvDecryptor)
    });
```

---

### Example 5: Development Environment

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCertificateFromFile("dev-secrets.pfx", "DevPassword")
            .CreateSelfSignedIfNotExist("CN=MyApp Dev")
            .WithKeyId("dev-secrets")
            .Build()
    });
```

---

## Best Practices

### 1. Pre-Encrypt All Production Secrets

**✅ DO:**
```bash
# Encrypt secrets externally (CI/CD, vault, security team)
cocoar-encrypt --cert prod.pfx --kid production-secrets --value "my-secret"
```

**❌ DON'T:**
```json
// Plaintext secrets will throw InvalidOperationException on Secret<T>.Open()
{
  "ApiKey": "plaintext-secret-value"
}
```

**Why:** Pre-encrypted envelopes ensure secrets never exist as plaintext in configuration files or memory until explicitly opened.

---

### 2. Use Folder-Based Certs for Rotation

**✅ DO:**
```csharp
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\prod", 
        passwordProvider: ctx => new[] { "password" });
// Add new certs to folder without restart, old secrets still work
// Uses kid-based folders: C:\certs\prod\production\*.pfx
```

**❌ DON'T:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/prod-2024-11.pfx", "password")
    .Build();
// Rotation requires code change and restart
```

---

### 3. Match Cache Duration to Data Sensitivity

**✅ DO:**
```csharp
// Critical data - no cache, load fresh every decrypt
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\critical", 
        passwordProvider: ctx => new[] { "password" },
        cacheDurationSeconds: 0);

// API keys - balanced 30-second cache
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\api", 
        passwordProvider: ctx => new[] { "password" },
        cacheDurationSeconds: 30);
```

**❌ DON'T:**
```csharp
// Everything cached for 1 hour - security risk for sensitive data!
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\all", 
        passwordProvider: ctx => new[] { "password" },
        cacheDurationSeconds: 3600);
```

---

### 4. Always Use `using` with SecretLease

**✅ DO:**
```csharp
using (var lease = config.ApiKey.Open())
{
    var key = lease.Value;
    await client.AuthenticateAsync(key);
}
// Memory zeroized automatically
```

**❌ DON'T:**
```csharp
var lease = config.ApiKey.Open();
var key = lease.Value;
await client.AuthenticateAsync(key);
// Memory NOT zeroized - potential leak!
```

---

### 5. Use Aliases for Migration

**✅ DO:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/unified.pfx", "password")
    .WithKeyId("production-v2")       // New standard
    .WithAdditionalKeyId("prod-v1")   // Old Kid (backward compat)
    .Build();
// Migrate gradually, no re-encryption needed
```

---

### 6. Use Descriptive Key IDs

**✅ DO:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/prod.pfx", "password")
    .WithKeyId("production-api-keys")  // Clear purpose
    .Build();

setup.Secrets()
    .UseCertificateFromFile("certs/db.pfx", "password")
    .WithKeyId("database-credentials")  // Clear purpose
    .Build();
```

**❌ DON'T:**
```csharp
setup.Secrets()
    .UseCertificateFromFile("certs/cert1.pfx", "password")
    .WithKeyId("cert1")  // Unclear purpose
    .Build();
```

---

### 7. Test Certificate Rotation

**✅ DO:**
```csharp
[Fact]
public void CertificateRotation_OldSecretsStillDecrypt()
{
    // Encrypt with old cert
    var encrypted = EncryptWith("old-cert.pfx");
    
    // Add new cert, keep old cert in folder
    AddCertToFolder("new-cert.pfx");
    
    // Verify old secrets still decrypt
    var decrypted = manager.Config.ApiKey.Open().Value;
    Assert.Equal(originalValue, decrypted);
}
```

---

### 8. Monitor and Audit

**✅ DO:**
```csharp
public class AuditingDecryptor : ISecretDecryptor<MyEnvelope>
{
    public byte[] Unprotect(MyEnvelope envelope)
    {
        _logger.LogInformation("Decrypting secret with Kid={Kid}", envelope.Kid);
        return _innerDecryptor.Unprotect(envelope);
    }
}
```

---

## See Also

- [Intelligent Certificate Caching](intelligent-certificate-caching.md) - Deep dive into folder-based caching architecture
- [Secure Data Transfer](secure-data-transfer.md) - Best practices for secret handling
- [Secrets Usage Examples](secrets-usage-examples.md) - Comprehensive examples

---

## Summary

The Secrets API provides:

✅ **Pre-encrypted envelopes** - Secrets encrypted at source, never plaintext in config  
✅ **Production-ready** - Certificate-based decryption with rotation support  
✅ **Flexible** - File-based, folder-based, or custom decryptors  
✅ **Secure** - Automatic memory zeroization, configurable cache TTLs  
✅ **Fail-fast** - Plaintext secrets throw `InvalidOperationException` on `.Open()`  
✅ **Cloud-ready** - Extensible for Azure Key Vault, AWS KMS, etc.

Start with `setup.Secrets()` and configure your decryption certificates!
