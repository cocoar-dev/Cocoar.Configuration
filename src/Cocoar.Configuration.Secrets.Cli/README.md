# Cocoar.Configuration.Secrets CLI

Command-line tool for encrypting secrets in JSON configuration files using hybrid RSA+AES encryption.

## Installation

### As .NET Global Tool (Recommended)

**From NuGet:**
```bash
dotnet tool install --global Cocoar.Configuration.Secrets.Cli
```

**From local build:**
```bash
cd src/Cocoar.Configuration.Secrets.Cli
dotnet pack
dotnet tool install --global --add-source ./nupkgs Cocoar.Configuration.Secrets.Cli
```

**Update to latest version:**
```bash
dotnet tool update --global Cocoar.Configuration.Secrets.Cli
```

**Uninstall:**
```bash
dotnet tool uninstall --global Cocoar.Configuration.Secrets.Cli
```

> **Note:** Requires .NET 9.0 SDK or runtime. For production servers without .NET SDK, self-contained binaries will be provided in future releases.

## Commands

### `generate-cert` - Generate Self-Signed Certificate

Creates a self-signed X.509 certificate for encryption/decryption.

**Usage:**
```bash
cocoar-secrets generate-cert --output mycert.pfx --password "MySecurePassword"
```

**Options:**
- `--output, -o` (Required) - Path where the PFX certificate will be saved
- `--password, -pwd` (Required) - Password to protect the PFX file
- `--subject, -s` - Certificate subject (default: "CN=Cocoar Secrets")
- `--valid-years, -y` - Certificate validity in years (default: 5)
- `--key-size, -k` - RSA key size: 2048, 3072, or 4096 bits (default: 2048)
- `--overwrite` - Overwrite existing certificate file

**Example:**
```bash
cocoar-secrets generate-cert \
  --output ./certs/production.pfx \
  --password "SecureP@ssw0rd" \
  --subject "CN=MyApp Production" \
  --valid-years 2 \
  --key-size 4096
```

### `encrypt` - Encrypt Value in JSON File

Encrypts a plaintext value and stores it at the specified property path in a JSON file.

**Usage:**
```bash
cocoar-secrets encrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --value "Server=prod;Database=db;Password=secret123" \
  --cert mycert.pfx \
  --password "MySecurePassword"
```

**Options:**
- `--file, -f` (Required) - Path to the JSON configuration file
- `--path, -p` (Required) - Property path using colon separator (e.g., "Database:ConnectionString")
- `--value, -v` (Required) - The plaintext value to encrypt
- `--cert, -c` (Required) - Path to the PFX certificate file for encryption
- `--password, -pwd` - Certificate password (will prompt if not provided)
- `--kid` - Key identifier for the certificate (default: "default")
- `--create` - Create the JSON file if it doesn't exist (prevents accidental typos)

**Property Path Format:**
Use colon (`:`) as separator to navigate nested objects:
- `"ApiKey"` - Top-level property
- `"Database:ConnectionString"` - Nested property
- `"Services:Stripe:SecretKey"` - Deeply nested property

**Examples:**

Encrypt a database connection string:
```bash
cocoar-secrets encrypt \
  --file appsettings.json \
  --path "ConnectionStrings:DefaultConnection" \
  --value "Server=prod-db;User=sa;Password=P@ssw0rd123" \
  --cert ./certs/prod.pfx \
  --password "CertPassword"
```

Create a new file with an encrypted API key:
```bash
cocoar-secrets encrypt \
  --file config/secrets.json \
  --path "Stripe:ApiKey" \
  --value "sk_live_51H..." \
  --cert ./certs/prod.pfx \
  --password "CertPassword" \
  --kid "stripe-prod" \
  --create
```

**Output Format:**

The encrypted value is stored as a `__cocoar_secret__` envelope:

```json
{
  "Database": {
    "ConnectionString": {
      "__cocoar_secret__": "v1",
      "kid": "default",
      "alg": "RSA-OAEP-AES256-GCM",
      "type": "utf8",
      "wk": "base64_wrapped_key...",
      "walg": "RSA-OAEP-256",
      "iv": "base64_iv...",
      "ct": "base64_ciphertext...",
      "tag": "base64_tag..."
    }
  }
}
```

### `decrypt` - Decrypt Value from JSON File

Decrypts an encrypted value and optionally re-encrypts it with a new certificate (certificate rotation).

**Usage:**
```bash
# Decrypt and display (warning: exposes secret in terminal)
cocoar-secrets decrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --old-cert oldcert.pfx \
  --old-password "OldPassword" \
  --show

# Decrypt and re-encrypt with new certificate (rotation)
cocoar-secrets decrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --old-cert oldcert.pfx \
  --old-password "OldPassword" \
  --new-cert newcert.pfx \
  --new-password "NewPassword"
```

**Options:**
- `--file, -f` (Required) - Path to the JSON configuration file
- `--path, -p` (Required) - Property path to the encrypted value
- `--old-cert` (Required) - Path to the certificate with private key for decryption
- `--old-password, -opwd` - Password for the old certificate
- `--new-cert` - Path to new certificate for re-encryption (certificate rotation)
- `--new-password, -npwd` - Password for the new certificate
- `--show` - Display the decrypted plaintext (⚠️ WARNING: exposes secret in terminal)

**Certificate Rotation Example:**
```bash
# Rotate all secrets to a new certificate
cocoar-secrets decrypt \
  --file appsettings.json \
  --path "ConnectionStrings:DefaultConnection" \
  --old-cert ./certs/old-prod.pfx \
  --old-password "OldPassword" \
  --new-cert ./certs/new-prod.pfx \
  --new-password "NewPassword"
```

### `cert-info` - Display Certificate Information

Display information about a certificate (placeholder - not yet implemented).

## Workflow Examples

### Initial Setup

1. **Generate a certificate:**
```bash
cocoar-secrets generate-cert \
  --output ./certs/dev.pfx \
  --password "DevPassword123" \
  --subject "CN=Development"
```

2. **Encrypt secrets in your configuration:**
```bash
cocoar-secrets encrypt \
  --file appsettings.Development.json \
  --path "Database:ConnectionString" \
  --value "Server=localhost;Database=DevDB;Trusted_Connection=true" \
  --cert ./certs/dev.pfx \
  --password "DevPassword123" \
  --create

cocoar-secrets encrypt \
  --file appsettings.Development.json \
  --path "ApiKeys:SendGrid" \
  --value "SG.abc123xyz..." \
  --cert ./certs/dev.pfx \
  --password "DevPassword123"
```

3. **Use in your application:**
```csharp
var config = new ConfigurationBuilder()
    .UseCocoar()
    .Secrets(secrets => secrets
        .UseHybridEncryption(hybrid => hybrid
            .UseCertificateFromFile("./certs/dev.pfx", "DevPassword123")
        )
    )
    .Build();

var connString = config.Get<Secret<string>>("Database:ConnectionString");
// Decrypts automatically when accessed
```

### Certificate Rotation

When rotating certificates (e.g., before expiry):

1. **Generate new certificate:**
```bash
cocoar-secrets generate-cert \
  --output ./certs/prod-2026.pfx \
  --password "NewProdPassword" \
  --subject "CN=Production 2026"
```

2. **Rotate each encrypted secret:**
```bash
# Rotate database connection string
cocoar-secrets decrypt \
  --file appsettings.Production.json \
  --path "Database:ConnectionString" \
  --old-cert ./certs/prod-2025.pfx \
  --old-password "OldProdPassword" \
  --new-cert ./certs/prod-2026.pfx \
  --new-password "NewProdPassword"

# Rotate API keys
cocoar-secrets decrypt \
  --file appsettings.Production.json \
  --path "ApiKeys:Stripe" \
  --old-cert ./certs/prod-2025.pfx \
  --old-password "OldProdPassword" \
  --new-cert ./certs/prod-2026.pfx \
  --new-password "NewProdPassword"
```

3. **Deploy new certificate with your application**

### CI/CD Integration

Store the certificate securely (e.g., Azure Key Vault, AWS Secrets Manager) and retrieve it during deployment:

```bash
# Example: GitHub Actions
- name: Decrypt secrets for deployment
  run: |
    echo "${{ secrets.PROD_CERT_BASE64 }}" | base64 -d > prod.pfx
    
    cocoar-secrets decrypt \
      --file appsettings.Production.json \
      --path "Database:ConnectionString" \
      --old-cert prod.pfx \
      --old-password "${{ secrets.CERT_PASSWORD }}" \
      --show
```

## Security Best Practices

### ✅ DO:
- **Store certificates securely** - Use Key Vault, Secrets Manager, or encrypted storage
- **Use strong passwords** - Minimum 16 characters with complexity
- **Rotate certificates regularly** - Before expiry, or annually for production
- **Use different certificates per environment** - Dev, staging, production should have separate certs
- **Keep private keys private** - Never commit certificates with private keys to source control
- **Use `--create` flag** - Prevents accidental file creation from typos
- **Audit certificate usage** - Track which certificates are used where

### ❌ DON'T:
- **Commit certificates to Git** - Add `*.pfx` to `.gitignore`
- **Use the same certificate for all environments** - Separate dev/staging/prod
- **Share certificate passwords insecurely** - Use secure secret management
- **Use `--show` in scripts** - Only use interactively when absolutely necessary
- **Skip certificate validation** - Verify certificate before using in production

## Encrypted File Format

The CLI creates JSON files with encrypted secrets in the `cocoar.secret` envelope format:

```json
{
  "type": "cocoar.secret",            // Envelope discriminator
  "version": 1,                        // Envelope format version
  "kid": "default",                  // Key identifier (selects certificate / protector)
  "alg": "RSA-OAEP-AES256-GCM",     // Encryption algorithm profile
  "contentType": "text/plain; charset=utf-8", // Plaintext encoding (optional)
  "wk": "...",                       // Wrapped (encrypted) AES key (base64)
  "walg": "RSA-OAEP-256",           // Key wrapping algorithm
  "iv": "...",                       // AES-GCM initialization vector (base64)
  "ct": "...",                       // Ciphertext (base64)
  "tag": "..."                       // AES-GCM authentication tag (base64)
}
```

**Encryption Method:**
- **Key Wrapping**: RSA-OAEP-SHA256 (wraps the AES key)
- **Data Encryption**: AES-256-GCM (encrypts the actual data)
- **Encoding**: Base64 for all binary data

This hybrid approach combines:
- **RSA security** - Strong asymmetric encryption for key distribution
- **AES performance** - Fast symmetric encryption for data
- **GCM authentication** - Ensures data integrity and authenticity

## Architecture

The CLI is a thin wrapper over the `Cocoar.Configuration.X509Encryption` library:

```
CLI (cocoar-secrets)
  └─> Cocoar.Configuration.X509Encryption
       ├── X509CertificateGenerator  (generate-cert)
       ├── X509HybridCrypto          (encryption/decryption)
       └── JsonSecretsEditor         (JSON file manipulation)
```

This means:
- **Tests** can use `JsonSecretsEditor` directly without the CLI
- **PowerShell modules** can reference the library for scripting
- **Custom tooling** can build on the same foundation

## Troubleshooting

### Certificate not found
```
Error: Certificate file not found: mycert.pfx
```
**Solution:** Verify the path is correct and the file exists.

### JSON file not found (without --create)
```
Error: JSON file not found: config.json. Use --create to create a new file.
```
**Solution:** Either create the file first, or use `--create` flag to create it automatically.

### Invalid certificate password
```
Error: The specified network password is not correct
```
**Solution:** Verify the certificate password is correct.

### Certificate has no private key
```
Error: Certificate must have a private key for encryption/decryption
```
**Solution:** Ensure you're using a PFX file that contains the private key, not just a public certificate.

### Property path not found
```
Error: Property 'ApiKey' not found in path 'Settings:ApiKey'
```
**Solution:** Verify the property path matches the JSON structure. Use `--create` for new files.

## Related Documentation

- [Cocoar.Configuration.Secrets Library](../Cocoar.Configuration.Secrets/README.md)
- [Secrets Usage Examples](../../docs/secrets-usage-examples.md)
- [Migration Guide](../../docs/migration-v2-to-v3.md)

## License

This project is part of the Cocoar.Configuration library and is licensed under the same terms.
