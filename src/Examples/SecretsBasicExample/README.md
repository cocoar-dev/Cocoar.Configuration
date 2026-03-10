# Secrets Basic Example

**Pre-Encrypted Secrets** - Demonstrates secure secret handling with encrypted envelopes.

## What This Example Demonstrates

- ✅ Loading pre-encrypted secrets from configuration files
- ✅ Password-less certificate-based decryption setup
- ✅ Proper `SecretLease` usage with `using` statements
- ✅ Memory zeroization for security
- ✅ `Secret<T>` for any JSON-serializable type

## Quick Start

```bash
dotnet run --project Examples\SecretsBasicExample\SecretsBasicExample.csproj
```

## Key Code Snippet

```csharp
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppConfig>().FromFile("appsettings.json")
    ])
    .UseSecretsSetup(secrets => secrets
        .UseCertificateFromFile("secrets.pfx")  // Password-less certificate
        .WithKeyId("dev-secrets")));

var config = manager.GetConfig<AppConfig>();

// Access secrets securely
using (var lease = config.Database.ApiKey.Open())
{
    var key = lease.Value;  // Use the secret
    // ... use it with APIs, database connections, etc.
}
// Memory automatically zeroized after 'using' block
```

## Configuration File Example

Your `appsettings.json` must contain pre-encrypted envelopes:

```json
{
  "Database": {
    "ApiKey": {
      "_cocoar_secret": "v1",
      "kid": "dev-secrets",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "utf8",
      "createdAt": "2024-11-01T12:34:56Z",
      "iv": "AbCdEf...",
      "ct": "XyZ123...",
      "tag": "DeF456...",
      "wk": "GhI789..."
    }
  }
}
```

## What Happens Under the Hood

1. Configuration system loads JSON with `_cocoar_secret` marker
2. `SecretJsonConverter` detects envelope and creates `Secret<T>` instance
3. Secret remains encrypted in memory until `.Open()` is called
4. Decryption happens on-demand using registered certificate
5. Secrets shown as `***` when printed
6. Memory zeroized when `SecretLease` is disposed

## Security Architecture

**Pre-encrypted envelopes only** - Secrets must be encrypted by external systems:
- CI/CD pipelines (Azure DevOps, GitHub Actions, Jenkins)
- Security teams using encryption tools
- Cloud vaults (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)

**Plaintext detection** - If you accidentally provide plaintext secrets instead of envelopes, `Secret<T>.Open()` will throw `InvalidOperationException` with a clear error message.

**Password-less certificates** - Certificates are protected by file permissions (`chmod 600` on Linux/macOS, NTFS permissions on Windows) and full-disk encryption (BitLocker/LUKS/FileVault).

## Use Case

**All environments** - Development, staging, and production all use pre-encrypted envelopes for consistent security.

## See Also

- [SecretsCertificateExample](../SecretsCertificateExample/) - Advanced certificate management
- [Secrets CLI](../../Cocoar.Configuration.Secrets.Cli/README.md) - Command-line encryption/decryption tools

