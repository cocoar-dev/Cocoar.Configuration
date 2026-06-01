# Cocoar.Configuration.Secrets CLI

Command-line tool for managing encrypted secrets in JSON configuration files using X.509 certificate-based hybrid encryption (RSA-OAEP + AES-256-GCM).

## Why This Tool Exists

The Secrets system expects **pre-encrypted envelopes** in configuration files. This CLI tool:
- **Generates** self-signed certificates for development and testing
- **Encrypts** secrets in JSON files before deployment
- **Decrypts** secrets for verification and troubleshooting
- **Converts** certificate formats and manages certificate operations

**At runtime**, the Cocoar.Configuration.Secrets library automatically decrypts these pre-encrypted secrets on-demand.

## Installation

```bash
# Install globally
dotnet tool install --global Cocoar.Configuration.Secrets.Cli

# Update
dotnet tool update --global Cocoar.Configuration.Secrets.Cli

# Uninstall
dotnet tool uninstall --global Cocoar.Configuration.Secrets.Cli
```

**Requirements:** .NET 9.0 SDK or runtime

## Quick Start

```bash
# Generate a password-less certificate (industry standard)
cocoar-secrets generate-cert -o secrets.pfx

# Encrypt a secret in a JSON file
cocoar-secrets encrypt -f appsettings.json -p Database:ConnectionString -v "Server=prod;Password=secret" -c secrets.pfx

# View decrypted value (doesn't modify file)
cocoar-secrets decrypt -f appsettings.json -p Database:ConnectionString -c secrets.pfx

# Get certificate information
cocoar-secrets cert-info -i secrets.pfx
```

---

## Available Commands

Use `cocoar-secrets <command> --help` for detailed options.

### Certificate Management

- **`generate-cert`** - Generate password-less self-signed certificates
- **`convert-cert`** - Convert formats (PFX ↔ PEM) and manage passwords
- **`cert-info`** - Display certificate details and status

### Secret Operations

- **`encrypt`** - Encrypt values in JSON files
- **`decrypt`** - Decrypt and view values (or replace with `--replace`)

---

## Key Concepts

### Password-less Certificates (Recommended)

**Industry standard approach** used by nginx, PostgreSQL, Kubernetes, Docker:
- Generate: `cocoar-secrets generate-cert -o cert.pfx` (no password)
- Protect via **file permissions**: `chmod 600 cert.pfx` (Linux/macOS), NTFS permissions (Windows)
- Enable **full-disk encryption**: BitLocker (Windows), LUKS (Linux), FileVault (macOS)

**Why password-less?**
- ✅ Simpler operations (no password management infrastructure)
- ✅ No bootstrapping problem (passwords are secrets too—where would you store them?)
- ✅ Same security level when combined with file permissions + disk encryption

**Legacy systems:** Use `convert-cert` to add passwords if required by legacy infrastructure.

### Certificate Formats

- **PFX (.pfx, .p12)** - PKCS#12 format, contains certificate + private key in single file
- **PEM (.crt, .cer, .pem + .key)** - Separate certificate and private key files

The CLI auto-detects format from file extension or use `--format` to override.

### Encrypted Envelope Format

Secrets are stored as `__cocoar_secret__` envelopes in JSON:

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

**At runtime**, the Cocoar.Configuration.Secrets library recognizes these envelopes and decrypts on-demand when you call `Secret<T>.Open()`.


---

## Security Best Practices

### Certificate Protection

✅ **DO:**
- Use password-less certificates (industry standard)
- Set file permissions: `chmod 600 *.pfx` (Linux/macOS)
- Enable full-disk encryption (BitLocker/LUKS/FileVault)
- Grant access only to application service account
- Store certificates in Key Vault or Secrets Manager
- Use different certificates per environment (dev, staging, production)
- Add `*.pfx`, `*.pem`, `*.key` to `.gitignore`

❌ **DON'T:**
- Commit certificates to source control
- Use password-protected certificates unless required by legacy systems
- Share certificates across environments
- Grant unnecessary file permissions

### Operations

✅ **DO:**
- Keep encrypted config files in source control
- Use `--create` flag to prevent typos
- Test decrypt operations in non-production first
- Back up certificates before rotation

❌ **DON'T:**
- Store plaintext secrets in source control
- Run `decrypt --replace` without backing up first
- Display decrypted values in CI/CD logs


---

## Troubleshooting

**Certificate not found:**
```
Error: Certificate file not found: mycert.pfx
```
→ Verify file path is correct

**Certificate appears password-protected:**
```
⚠️  Certificate appears to be password-protected.
```
→ Provide password with `-pwd` or use `convert-cert` to remove password

**Property path not found:**
```
Error: Property path 'Settings:ApiKey' not found in JSON
```
→ For `encrypt`: verify parent object exists or use `--create`  
→ For `decrypt`: verify path matches encrypted value location

**Decryption fails:**
```
Error: Failed to decrypt: MAC validation failed
```
→ Verify using correct certificate (must match encryption cert)  
→ Check encrypted envelope hasn't been manually modified

Run any command with `--help` for detailed usage information.

---

## Related Documentation

- [Intelligent Certificate Caching](../Cocoar.Configuration.Secrets/intelligent-certificate-caching.md) - Advanced certificate management
- [Cocoar.Configuration.Secrets](../Cocoar.Configuration.Secrets/README.md) - Runtime library documentation
- [Examples](../../Examples/) - Working code examples

---

## Support

- **Issues**: [GitHub Issues](https://github.com/cocoar-dev/Cocoar.Configuration/issues)
- **Discussions**: [GitHub Discussions](https://github.com/cocoar-dev/Cocoar.Configuration/discussions)
- **Contributing**: [CONTRIBUTING.md](../../CONTRIBUTING.md)
- **Security**: [SECURITY.md](../../SECURITY.md)

---

**License:** Apache 2.0 - See [LICENSE](../../LICENSE)
