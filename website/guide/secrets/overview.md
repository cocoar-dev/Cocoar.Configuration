# Secrets Overview

Cocoar.Configuration has built-in support for secrets — configuration values that are encrypted at rest and protected in memory. No separate package needed.

## The Problem

Storing secrets in plaintext config files is a security risk:

```json
{
  "Database": {
    "ConnectionString": "Server=prod;Password=s3cret"
  }
}
```

Anyone with file access can read the password. It sits in memory as a `string` — visible in heap dumps, never garbage collected reliably, impossible to zero.

## The Cocoar Approach

Secrets are encrypted in your config files using X.509 certificates:

```json
{
  "Database": {
    "ConnectionString": {
      "type": "cocoar.secret",
      "kid": "prod-secrets",
      "alg": "RSA-OAEP-AES256-GCM",
      "wk": "base64...",
      "iv": "base64...",
      "ct": "base64...",
      "tag": "base64..."
    }
  }
}
```

In your C# class, declare the property as `Secret<T>`:

```csharp
public class DatabaseConfig
{
    public required Secret<string> ConnectionString { get; init; }
}
```

Access the decrypted value through a lease:

```csharp
public class MyService(DatabaseConfig config)
{
    public void Connect()
    {
        using var lease = config.ConnectionString.Open();
        var connectionString = lease.Value;
        // Use it — value is zeroed when the lease is disposed
    }
}
```

## Key Concepts

| Concept | Description |
|---|---|
| [`Secret<T>`](/guide/secrets/secret-type) | A property type that holds an encrypted value |
| [`SecretLease<T>`](/guide/secrets/secret-type#leases) | Temporary access to the decrypted value — dispose to zero memory |
| [Encryption Setup](/guide/secrets/encryption-setup) | Configure certificates for encryption/decryption |
| [CLI Tools](/guide/secrets/cli) | Encrypt values and manage certificates from the command line |
| [Security Model](/guide/secrets/security-model) | Memory safety, zeroization, threat model |

## Quick Setup

### 1. Configure encryption

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<DatabaseConfig>().FromFile("appsettings.json"),
    ])
    .UseSecretsSetup(secrets => secrets
        .UseCertificateFromFile("certs/prod.pfx")
        .WithKeyId("prod-secrets")));
```

### 2. Encrypt a value

```shell
dotnet cocoar-secrets encrypt \
    --value "Server=prod;Password=s3cret" \
    --cert certs/prod.pfx \
    --kid prod-secrets
```

### 3. Paste the output into your config file

The CLI outputs the encrypted JSON envelope. Replace the plaintext value with it.

### Development Mode

For local development, skip encryption entirely:

```csharp
.UseSecretsSetup(secrets => secrets.AllowPlaintext())
```

With `AllowPlaintext()`, `Secret<string>` properties deserialize from plain JSON strings. A trace warning is emitted to remind you this isn't for production.
