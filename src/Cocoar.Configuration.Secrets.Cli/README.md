# Cocoar.Configuration.Secrets CLI

Command-line tool for encrypting and decrypting secrets in JSON configuration files using hybrid RSA+AES encryption with X.509 certificates.

## Features

- **Hybrid Encryption**: RSA + AES-256-GCM for secure and performant encryption
- **Certificate-Based**: Uses X.509 certificates (PFX or PEM format)
- **Smart JSON Type Handling**: Preserves types for strings, numbers, booleans, arrays, and objects
- **Multiple Secrets per File**: Each secret can use a different certificate
- **Safe Defaults**: Non-destructive operations by default, explicit flags for modifications
- **Two Workflows**: Encrypt inline with `-v` flag or encrypt existing values in-place

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

## Quick Start

```bash
# 1. Generate a certificate
cocoar-secrets generate-cert -o config-cert.pfx -pwd MySecurePassword

# 2. Encrypt a database connection string
cocoar-secrets encrypt -f appsettings.json -p Database:ConnectionString -v "Server=prod;Password=secret" -c config-cert.pfx -pwd MySecurePassword

# 3. View decrypted value (doesn't modify file)
cocoar-secrets decrypt -f appsettings.json -p Database:ConnectionString -c config-cert.pfx -pwd MySecurePassword

# 4. Decrypt back to plaintext in file (explicit with --replace)
cocoar-secrets decrypt -f appsettings.json -p Database:ConnectionString -c config-cert.pfx -pwd MySecurePassword --replace
```

---

## Commands

### `generate-cert` - Generate Self-Signed Certificate

Creates a self-signed X.509 certificate for encryption/decryption.

**Usage:**
```bash
cocoar-secrets generate-cert -o mycert.pfx -pwd "MySecurePassword"
```

**Options:**
- `--output, -o` (Required) - Path where the certificate will be saved
- `--password, -pwd` - Password to protect the PFX file (required for PFX format)
- `--format` - Output format: `pfx` (default), `pem`, or `auto` (detects from file extension)
- `--subject, -s` - Certificate subject (default: `CN=Cocoar Secrets`)
- `--valid-years` - Certificate validity in years (default: `1`)
- `--key-size` - RSA key size: `2048`, `3072`, or `4096` bits (default: `2048`)
- `--overwrite` - Overwrite existing certificate file without prompt

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

Encrypts a plaintext value and stores it as an encrypted envelope at the specified property path in a JSON file.

**Two Workflows:**

1. **Inline Encryption** (with `-v` flag):
   ```bash
   cocoar-secrets encrypt -f config.json -p Database:Password -v "secret123" -c cert.pfx -pwd CertPass
   ```

2. **In-Place Encryption** (without `-v` flag):
   ```bash
   # First, add plaintext to your JSON file
   echo '{"Database": {"Password": "secret123"}}' > config.json
   
   # Then encrypt the existing value
   cocoar-secrets encrypt -f config.json -p Database:Password -c cert.pfx -pwd CertPass
   ```

**Options:**
- `--file, -f` (Required) - Path to the JSON configuration file
- `--path, -p` (Required) - Property path using colon separator (e.g., `Database:ConnectionString`)
- `--value, -v` (Optional) - The plaintext value to encrypt. If omitted, encrypts the existing value at the specified path
- `--cert, -c` (Required) - Path to the PFX certificate file for encryption
- `--password, -pwd` - Certificate password (will prompt if not provided)
- `--kid` - Key identifier for the certificate (default: `default`)
- `--create` - Create the JSON file if it doesn't exist

**Property Path Format:**
Use colon (`:`) to navigate nested objects:
- `ApiKey` → Top-level property
- `Database:ConnectionString` → Nested property
- `Services:Stripe:SecretKey` → Deeply nested property

**JSON Type Handling:**

The tool intelligently handles different JSON value types:

```bash
# String (auto-quoted)
cocoar-secrets encrypt -f config.json -p Name -v Alice -c cert.pfx -pwd Pass
# Result: "Alice"

# Number (preserved as number)
cocoar-secrets encrypt -f config.json -p Port -v 5432 -c cert.pfx -pwd Pass
# Result: 5432

# Boolean (preserved as boolean)
cocoar-secrets encrypt -f config.json -p EnableDebug -v true -c cert.pfx -pwd Pass
# Result: true

# Explicit string (use quotes with shell escaping)
cocoar-secrets encrypt -f config.json -p PortAsString -v '"5432"' -c cert.pfx -pwd Pass
# Result: "5432" (string, not number)

# Array or Object (valid JSON preserved)
cocoar-secrets encrypt -f config.json -p Tags -v '["prod","db"]' -c cert.pfx -pwd Pass
# Result: ["prod","db"]
```

**Examples:**

Encrypt a connection string:
```bash
cocoar-secrets encrypt \
  -f appsettings.json \
  -p ConnectionStrings:DefaultConnection \
  -v "Server=prod-db;User=sa;Password=P@ss123" \
  -c ./certs/prod.pfx \
  -pwd "CertPassword"
```

In-place workflow (add plaintext first, then encrypt):
```json
{
  "Database": {
    "ConnectionString": "Server=prod;Password=secret"
  }
}
```
```bash
cocoar-secrets encrypt -f appsettings.json -p Database:ConnectionString -c cert.pfx -pwd Pass
```

Create a new encrypted secrets file:
```bash
cocoar-secrets encrypt \
  -f config/secrets.json \
  -p Stripe:ApiKey \
  -v "sk_live_51H..." \
  -c ./certs/prod.pfx \
  -pwd "CertPassword" \
  --kid "stripe-prod" \
  --create
```

**Encrypted Envelope Format:**

Values are stored as `__cocoar_secret__` envelopes:

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

Decrypts an encrypted value. By default, displays the plaintext without modifying the file. Use `--replace` to explicitly replace the encrypted envelope with plaintext in the JSON file.

**Usage:**
```bash
# Default: Show decrypted value (safe, doesn't modify file)
cocoar-secrets decrypt -f config.json -p Database:Password -c cert.pfx -pwd CertPass

# Explicit: Replace encrypted value with plaintext in file
cocoar-secrets decrypt -f config.json -p Database:Password -c cert.pfx -pwd CertPass --replace
```

**Options:**
- `--file, -f` (Required) - Path to the JSON configuration file
- `--path, -p` (Required) - Property path to the encrypted value
- `--cert, -c` (Required) - Path to the PFX certificate file with private key for decryption
- `--password, -pwd` - Certificate password (will prompt if not provided)
- `--replace` - Replace the encrypted envelope with plaintext in the JSON file (⚠️ WARNING: modifies file)

**Safe Default Behavior:**

By default, `decrypt` only displays the value without modifying the file:

```bash
cocoar-secrets decrypt -f appsettings.json -p Database:Password -c cert.pfx -pwd Pass
```
Output:
```
✓ Successfully decrypted value at 'Database:Password'

Decrypted value:
MySecretPassword123
```

The JSON file remains unchanged with the encrypted envelope intact.

**Explicit File Modification:**

To replace the encrypted value with plaintext, use `--replace`:

```bash
cocoar-secrets decrypt -f appsettings.json -p Database:Password -c cert.pfx -pwd Pass --replace
```

Before (encrypted):
```json
{
  "Database": {
    "Password": {
      "__cocoar_secret__": "v1",
      "kid": "default",
      "ct": "base64_ciphertext..."
    }
  }
}
```

After (plaintext):
```json
{
  "Database": {
    "Password": "MySecretPassword123"
  }
}
```

**Examples:**

View a decrypted connection string:
```bash
cocoar-secrets decrypt \
  -f appsettings.json \
  -p ConnectionStrings:DefaultConnection \
  -c ./certs/prod.pfx \
  -pwd "CertPassword"
```

Decrypt all secrets back to plaintext (run for each secret):
```bash
cocoar-secrets decrypt -f config.json -p Database:Password -c cert.pfx -pwd Pass --replace
cocoar-secrets decrypt -f config.json -p ApiKeys:Stripe -c cert.pfx -pwd Pass --replace
```

---

## Complete Workflow Examples

### Development Workflow

**1. Generate a certificate:**
```bash
cocoar-secrets generate-cert -o ./certs/dev.pfx -pwd "DevPass123" --subject "CN=Development"
```

**2. Add plaintext secrets to your JSON file:**
```json
{
  "Database": {
    "ConnectionString": "Server=localhost;Database=DevDB;User=dev;Password=devpass"
  },
  "ApiKeys": {
    "Stripe": "sk_test_123...",
    "SendGrid": "SG.abc123..."
  }
}
```

**3. Encrypt secrets in-place:**
```bash
cocoar-secrets encrypt -f appsettings.Development.json -p Database:ConnectionString -c ./certs/dev.pfx -pwd DevPass123
cocoar-secrets encrypt -f appsettings.Development.json -p ApiKeys:Stripe -c ./certs/dev.pfx -pwd DevPass123
cocoar-secrets encrypt -f appsettings.Development.json -p ApiKeys:SendGrid -c ./certs/dev.pfx -pwd DevPass123
```

**4. Commit encrypted JSON to source control** (certificate stays local or in secure vault)

**5. Use in your application:**
```csharp
var config = new ConfigurationBuilder()
    .UseCocoar()
    .Secrets(secrets => secrets
        .UseHybridEncryption(hybrid => hybrid
            .UseCertificateFromFile("./certs/dev.pfx", "DevPass123")
        )
    )
    .Build();

var connString = config.Get<Secret<string>>("Database:ConnectionString");
// Automatically decrypts when accessed
```

### Certificate Rotation Workflow

Certificate rotation requires re-encrypting secrets with a new certificate. Since each secret can use a different certificate (via `kid`), this is typically done manually per secret:

**Note:** Direct certificate rotation is not currently supported. To rotate certificates:

1. **Decrypt to plaintext:**
   ```bash
   cocoar-secrets decrypt -f config.json -p Database:Password -c old-cert.pfx -pwd OldPass --replace
   ```

2. **Re-encrypt with new certificate:**
   ```bash
   cocoar-secrets encrypt -f config.json -p Database:Password -c new-cert.pfx -pwd NewPass
   ```

This two-step approach ensures clarity and prevents accidental data loss when rotating certificates.

### CI/CD Integration

**GitHub Actions Example:**

```yaml
name: Deploy with Secrets

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'
      
      - name: Install CLI tool
        run: dotnet tool install --global Cocoar.Configuration.Secrets.Cli
      
      - name: Restore certificate from secrets
        run: |
          echo "${{ secrets.PROD_CERT_BASE64 }}" | base64 -d > prod.pfx
      
      - name: View decrypted config (for verification)
        run: |
          cocoar-secrets decrypt \
            -f appsettings.Production.json \
            -p Database:ConnectionString \
            -c prod.pfx \
            -pwd "${{ secrets.CERT_PASSWORD }}"
      
      - name: Deploy application
        run: |
          # Deploy with encrypted config
          # Certificate (prod.pfx) must be deployed alongside the app
          dotnet publish -c Release
```

---

## Security Best Practices

### Certificate Management

✅ **DO:**
- Store certificates securely (Key Vault, Secrets Manager, encrypted storage)
- Use strong passwords (minimum 16 characters with complexity)
- Rotate certificates before expiry (recommend annually for production)
- Use different certificates per environment (dev, staging, production)
- Keep private keys private (never commit PFX files to source control)
- Use meaningful certificate subjects and key identifiers
- Audit certificate usage regularly

❌ **DON'T:**
- Commit certificates to Git (add `*.pfx`, `*.pem` to `.gitignore`)
- Use the same certificate across all environments
- Share certificate passwords insecurely (no email, Slack, etc.)
- Leave expired certificates in production
- Use weak passwords or leave passwords empty

### File Handling

✅ **DO:**
- Use `--create` flag to prevent typos creating unintended files
- Verify file paths before running encrypt/decrypt operations
- Keep encrypted config files in source control
- Back up certificates before rotation

❌ **DON'T:**
- Run decrypt with `--replace` without backing up first
- Store plaintext secrets in source control
- Use default/guessable property paths

### Operational Security

✅ **DO:**
- Test decrypt operations in non-production first
- Use CI/CD secret management for certificate passwords
- Monitor certificate expiry dates
- Document which certificates are used where

❌ **DON'T:**
- Run CLI commands with secrets in shell history (use prompts for passwords)
- Display decrypted values in CI/CD logs
- Use `--replace` in automated scripts without careful review

---

## Encrypted Envelope Format

Encrypted values are stored as `__cocoar_secret__` envelopes in your JSON files:

```json
{
  "__cocoar_secret__": "v1",
  "kid": "default",
  "alg": "RSA-OAEP-AES256-GCM",
  "type": "utf8",
  "wk": "base64_wrapped_aes_key...",
  "walg": "RSA-OAEP-256",
  "iv": "base64_initialization_vector...",
  "ct": "base64_ciphertext...",
  "tag": "base64_authentication_tag..."
}
```

**Field Descriptions:**
- `__cocoar_secret__` - Envelope version (currently `v1`)
- `kid` - Key identifier to select the correct certificate/protector
- `alg` - Encryption algorithm profile
- `type` - Content type (e.g., `utf8` for text)
- `wk` - Wrapped (encrypted) AES-256 key using RSA
- `walg` - Key wrapping algorithm (RSA-OAEP with SHA-256)
- `iv` - AES-GCM initialization vector (12 bytes, base64)
- `ct` - Encrypted ciphertext (base64)
- `tag` - AES-GCM authentication tag for integrity (16 bytes, base64)

**Hybrid Encryption Process:**
1. Generate random 256-bit AES key
2. Encrypt data with AES-256-GCM (fast, authenticated)
3. Wrap AES key with certificate's RSA public key (secure key distribution)
4. Store wrapped key + encrypted data in envelope

This combines RSA security with AES performance and GCM authentication.

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

---

## Troubleshooting

### Certificate not found
```
Error: Certificate file not found: mycert.pfx
```
**Solution:** Verify the file path is correct and the certificate exists.

### Invalid certificate password
```
Error: The specified network password is not correct
```
**Solution:** Verify the certificate password. If no password was set during generation, omit the `--password` flag to be prompted.

### Certificate has no private key
```
Error: Certificate must have a private key for decryption
```
**Solution:** Ensure you're using a PFX file with the private key, not just the public certificate. Use the same PFX file that was used for encryption.

### Property path not found
```
Error: Property path 'Settings:ApiKey' not found in JSON
```
**Solution:** 
- For encrypt: Verify the parent object exists, or use `--create` for new files
- For decrypt: Verify the property path matches the encrypted value location

### JSON file not found (without --create)
```
Error: JSON file not found: config.json
```
**Solution:** Either create the file first, or use `--create` flag with encrypt command.

### Invalid JSON syntax
```
Error: Failed to parse JSON file
```
**Solution:** Validate your JSON syntax. The file must contain a valid JSON object at the root level.

### Decryption fails
```
Error: Failed to decrypt: MAC validation failed
```
**Solution:** 
- Verify you're using the correct certificate (must match the one used for encryption)
- Verify the certificate password is correct
- Check that the encrypted envelope hasn't been manually modified

---

## FAQ

**Q: Can I use multiple certificates in the same file?**  
A: Yes! Each encrypted value can use a different certificate by specifying different `--kid` values during encryption.

**Q: How do I back up my certificates?**  
A: Store PFX files securely in a password manager, Key Vault, or encrypted backup. Keep the password separate from the certificate file.

**Q: Can I use this in production?**  
A: Yes. The hybrid encryption (RSA + AES-256-GCM) is production-grade. Ensure certificates are stored securely and rotated regularly.

**Q: What happens if I lose the certificate?**  
A: Encrypted values cannot be decrypted without the certificate's private key. Always back up certificates and store them securely.

**Q: Can I encrypt entire JSON objects?**  
A: Yes. Use `-v '{"key":"value"}'` to encrypt valid JSON objects or arrays. They will be preserved as structured data.

**Q: How do I rotate certificates?**  
A: Currently, decrypt with `--replace` to plaintext, then re-encrypt with the new certificate. A direct rotation command may be added in future releases.

**Q: Can the library decrypt without the CLI?**  
A: Yes. The `Cocoar.Configuration.Secrets` library automatically decrypts values at runtime using configured certificates. The CLI is for managing encrypted files, not runtime decryption.

---

## Related Documentation

- [Cocoar.Configuration.Secrets Library](../Cocoar.Configuration.Secrets/README.md) - Runtime secret decryption
- [Secrets Usage Examples](../../docs/secrets-usage-examples.md) - Code examples and patterns
- [Secrets API Reference](../../docs/secrets-api-reference.md) - Detailed API documentation
- [Migration Guide v2 → v3](../../docs/migration-v2-to-v3.md) - Breaking changes and migration steps

---

## Support & Contributing

- **Issues**: [GitHub Issues](https://github.com/cocoar-dev/Cocoar.Configuration/issues)
- **Discussions**: [GitHub Discussions](https://github.com/cocoar-dev/Cocoar.Configuration/discussions)
- **Contributing**: See [CONTRIBUTING.md](../../CONTRIBUTING.md)
- **Security**: Report vulnerabilities via [SECURITY.md](../../SECURITY.md)

---

## License

Part of the Cocoar.Configuration library. See [LICENSE](../../LICENSE) for details.
