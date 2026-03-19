# Cloud Providers

Native providers for Azure Key Vault and AWS Secrets Manager — so you can load secrets and configuration from your existing cloud KMS without building a custom provider.

## Why

Today, `Secret<T>` uses X.509 certificates for encryption. That works well for single deployments and on-premise setups. But many teams already have secrets in Azure Key Vault or AWS Secrets Manager and don't want to migrate them into certificate-encrypted JSON files.

Cloud providers bridge this gap: keep your secrets where they are, load them through Cocoar's rule system.

## Azure Key Vault Provider

```csharp
rule.For<DbConfig>().FromAzureKeyVault(vault =>
{
    vault.VaultUri = new Uri("https://my-vault.vault.azure.net/");
    vault.SecretName = "db-config";
})
```

Planned capabilities:
- Load individual secrets or entire secret groups
- Automatic refresh on secret rotation (via Key Vault change notifications)
- Managed Identity authentication (no credentials in config)
- Works alongside file-based rules — Key Vault overrides local defaults via normal layering

## AWS Secrets Manager Provider

```csharp
rule.For<DbConfig>().FromAwsSecretsManager(aws =>
{
    aws.SecretId = "prod/db-config";
    aws.Region = "eu-central-1";
})
```

Planned capabilities:
- Load secrets by ID or ARN
- Automatic rotation support
- IAM role-based authentication
- Regional failover support

## How They Fit In

Cloud providers are just providers — they plug into the existing rule system. You can layer them freely:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),                // Base config
    rule.For<AppSettings>().FromAzureKeyVault(v => v.SecretName = "app"), // Cloud overrides
    rule.For<AppSettings>().FromEnvironment("APP_"),                      // Local overrides
]
```

Same merge semantics, same health monitoring, same reactive updates.

## Can't Wait?

The [custom provider contract](/guide/providers/custom) is two methods. If you need Azure Key Vault or AWS today, you can build a provider in ~200 lines. The planned native providers will offer a polished, tested, production-ready experience — but you're not blocked.

## Status

Planned. These are the highest-priority items on the roadmap — they're the most common request and the primary adoption blocker for teams with existing cloud infrastructure.
