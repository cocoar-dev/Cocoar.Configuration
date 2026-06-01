# Secrets Certificate Example

**Production-ready secrets management** with certificate-based decryption and rotation.

## What This Example Demonstrates

- ✅ Multiple certificate configurations for different environments
- ✅ Certificate rotation with `UseCertificatesFromFolder`
- ✅ Key identifier (Kid) management and backward compatibility
- ✅ Certificate caching strategies for security/performance trade-offs

## Quick Start

```bash
dotnet run --project Examples\SecretsCertificateExample\SecretsCertificateExample.csproj
```

## Key Code Snippets

### Development Setup

First, generate a certificate using the CLI:
```bash
cocoar-secrets generate-cert -o certs/dev.pfx
```

Then configure the manager:
```csharp
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppConfig>().FromFile("appsettings.dev.json")
    ])
    .UseSecretsSetup(secrets => secrets
        .UseCertificateFromFile("certs/dev.pfx")
        .WithKeyId("dev-secrets")));
```

### Production Setup with Rotation

```csharp
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppConfig>().FromFile("appsettings.prod.json")
    ])
    // Folder-based with automatic rotation
    // Uses kid-based folders: C:\certs\prod\production-secrets\*.pfx
    .UseSecretsSetup(secrets => secrets
        .UseCertificatesFromFolder(@"C:\certs\prod",
            cacheDurationSeconds: 30)));
```

### Multi-Tier Security

```csharp
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppConfig>().FromFile("appsettings.json")
    ])
    // Critical secrets - no cache, load fresh every time
    .UseSecretsSetup(secrets => secrets
        .UseCertificatesFromFolder(@"C:\certs\pci",
            cacheDurationSeconds: 0)));  // Maximum security
```

## Certificate Management

### Certificate Roles

1. **Decryption Certificates** (Registered via `UseCertificateFromFile` / `UseCertificatesFromFolder`)
   - Decrypt pre-encrypted secrets from external sources
   - Identified by `kid` (key identifier)
   - Multiple certificates supported for rotation and multi-environment

### Benefits of Folder-Based Certificates

- **Zero-downtime rotation:** Add new certificate, old secrets still work
- **Reduced memory footprint:** Certificates cached with TTL
- **Automatic discovery:** No restart needed when certificates added
- **Intelligent caching:** Two-level cache (envelope hash + cert cache)

### Security vs Performance Trade-offs

| Cache Duration | Security Level | Use Case | Performance |
|----------------|---------------|----------|-------------|
| **0s** | Maximum | PCI-DSS, HIPAA | File I/O every decrypt |
| **5-30s** | High | API keys, credentials | 100-1000x faster |
| **60-300s** | Medium | Service credentials | Minimal I/O |
| **3600s+** | Low | Feature flags | Maximum performance |

## Configuration File Example

Your `appsettings.json` must contain pre-encrypted envelopes:

```json
{
  "Database": {
    "ConnectionString": {
      "_cocoar_secret": "v1",
      "kid": "production-secrets",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "utf8",
      "createdAt": "2024-11-01T12:34:56Z",
      "iv": "...",
      "ct": "...",
      "tag": "...",
      "wk": "..."
    }
  },
  "ApiKeys": {
    "Stripe": {
      "_cocoar_secret": "v1",
      "kid": "api-keys",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "utf8",
      "createdAt": "2024-11-01T12:34:56Z",
      "iv": "...",
      "ct": "...",
      "tag": "...",
      "wk": "..."
    }
  }
}
```

## Best Practices

1. **Pre-encrypt all production secrets** - Use CI/CD pipelines or security tools
2. **Use folder-based certificates** - Enable rotation without code changes
3. **Match cache duration to sensitivity** - Critical data = 0s cache, feature flags = 1 hour
4. **Use descriptive Kids** - `production-api-keys` > `cert1`
5. **Plan for rotation** - Use `WithAdditionalKeyId` for backward compatibility
6. **Test rotation** - Verify old secrets decrypt with new certificates
7. **Monitor and audit** - Log decryption operations for compliance

## Use Case

**Production environments** - Enterprise-grade secret management with:
- Certificate rotation
- Multi-tier security
- Compliance requirements (PCI-DSS, HIPAA)
- High availability

## See Also

- [Intelligent Certificate Caching](../../Cocoar.Configuration.Secrets/intelligent-certificate-caching.md) - Deep dive into caching architecture
- [Secrets CLI](../../Cocoar.Configuration.Secrets.Cli/README.md) - Command-line encryption/decryption tools
