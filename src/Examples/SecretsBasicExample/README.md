# Secrets Basic Example# Secrets Basic Example



**Pre-Encrypted Secrets** - Demonstrates secure secret handling with encrypted envelopes.**Zero-configuration AutoProtect** - The simplest way to use secrets in Cocoar Configuration.



## What This Example Demonstrates## What This Example Demonstrates



- ✅ Loading pre-encrypted secrets from configuration files- ✅ Zero-config setup - just call `setup.Secrets()`

- ✅ Certificate-based decryption setup- ✅ AutoProtect automatically creates a self-signed certificate

- ✅ Proper `SecretLease` usage with `using` statements- ✅ Plain-text secrets from JSON encrypted in-memory

- ✅ Memory zeroization for security- ✅ Proper `SecretLease` usage with `using` statements

- ✅ `Secret<T>` for any JSON-serializable type- ✅ Memory zeroization for security



## Quick Start## Quick Start



```bash```bash

dotnet run --project Examples\SecretsBasicExample\SecretsBasicExample.csprojdotnet run --project Examples\SecretsBasicExample\SecretsBasicExample.csproj

``````



## Key Code Snippet## Key Code Snippet



```csharp```csharp

var manager = new ConfigManager(rule => [var manager = new ConfigManager(rule => [

    rule.For<AppConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("appsettings.json"))    rule.For<AppConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("appsettings.json"))

], setup => [], setup => [

    setup.Secrets()    setup.Secrets()  // That's it! AutoProtect handles everything

        .UseCertificateFromFile("secrets.pfx", "DevPassword")]).Initialize();

        .WithKeyId("dev-secrets")

        .Build()var config = manager.GetConfig<AppConfig>();

]).Initialize();

// Access secrets securely

var config = manager.GetConfig<AppConfig>();using (var lease = config.Database.ApiKey.Open())

{

// Access secrets securely    var key = lease.Value;  // Use the secret

using (var lease = config.Database.ApiKey.Open())    // ... use it with APIs, database connections, etc.

{}

    var key = lease.Value;  // Use the secret// Memory automatically zeroized after 'using' block

    // ... use it with APIs, database connections, etc.```

}

// Memory automatically zeroized after 'using' block## What Happens Under the Hood

```

1. AutoProtect creates a self-signed certificate: `CN=Cocoar.Configuration.AutoProtect`

## Configuration File Example2. Certificate stored in: `CurrentUser\My` certificate store

3. Plain-text secrets from JSON are encrypted in-memory (NOT written to disk)

Your `appsettings.json` must contain pre-encrypted envelopes:4. Secrets shown as `***` when printed

5. Memory zeroized when `SecretLease` is disposed

```json

{## Use Case

  "Database": {

    "ApiKey": {**Development & Testing** - Minimal setup, maximum convenience. No certificate management needed!

      "_cocoar_secret": "v1",

      "kid": "dev-secrets",## See Also

      "alg": "RSA-OAEP-AES256-GCM",

      "type": "utf8",- [SecretsCertificateExample](../SecretsCertificateExample/) - Production setup with certificate-based decryption

      "createdAt": "2024-11-01T12:34:56Z",- [Secrets API Reference](../../../docs/secrets-api-reference.md) - Complete API documentation

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

## Use Case

**All environments** - Development, staging, and production all use pre-encrypted envelopes for consistent security.

## See Also

- [SecretsCertificateExample](../SecretsCertificateExample/) - Advanced certificate management
- [Secrets API Reference](../../../docs/secrets-api-reference.md) - Complete API documentation
