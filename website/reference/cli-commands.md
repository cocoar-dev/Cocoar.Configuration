---
description: cocoar-secrets CLI reference — encrypt, decrypt, generate-cert, convert-cert, cert-info; options, exit codes, RSA-OAEP-SHA256 + AES-256-GCM envelope
---

# CLI Commands Reference

## Installation

```shell
dotnet tool install -g Cocoar.Configuration.Secrets.Cli
```

All commands are invoked as `cocoar-secrets <command>`.

## encrypt

Encrypt a value and set it at a property path in a JSON file.

```shell
cocoar-secrets encrypt --file <path> --path <property-path> --cert <cert-path> [options]
```

| Option | Alias | Type | Default | Description |
|---|---|---|---|---|
| `--file` | `-f` | string | *required* | Path to the JSON configuration file |
| `--path` | `-p` | string | *required* | Property path (e.g. `Database:ConnectionString`) |
| `--cert` | `-c` | string | *required* | Path to the PFX certificate file |
| `--value` | `-v` | string | — | Plaintext value to encrypt. If omitted, encrypts the existing value at the path |
| `--password` | `-pwd` | string | — | Certificate password (prompts if not provided) |
| `--kid` | | string | `"default"` | Key identifier for the certificate |
| `--create` | | bool | `false` | Create the JSON file if it doesn't exist |

**Examples:**

```shell
# Encrypt a connection string
cocoar-secrets encrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --value "Server=prod;Database=mydb;Password=secret" \
  --cert cert.pfx \
  --kid "prod-2026"

# Encrypt from stdin (avoids shell history)
echo -n "my-secret-value" | cocoar-secrets encrypt \
  --file appsettings.json \
  --path "ApiKeys:Stripe" \
  --cert cert.pfx

# Encrypt existing plaintext value in-place
cocoar-secrets encrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --cert cert.pfx
```

## decrypt

Decrypt an encrypted value from a JSON file.

```shell
cocoar-secrets decrypt --file <path> --path <property-path> --cert <cert-path> [options]
```

| Option | Alias | Type | Default | Description |
|---|---|---|---|---|
| `--file` | `-f` | string | *required* | Path to the JSON configuration file |
| `--path` | `-p` | string | *required* | Property path of the encrypted value |
| `--cert` | `-c` | string | *required* | Path to the PFX certificate file |
| `--password` | `-pwd` | string | — | Certificate password (prompts if not provided) |
| `--replace` | | bool | `false` | Replace the encrypted value with plaintext in the file |

::: warning
`--replace` modifies the file irreversibly. The encrypted envelope is replaced with the plaintext value.
:::

**Examples:**

```shell
# Display decrypted value (read-only)
cocoar-secrets decrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --cert cert.pfx

# Replace encrypted value with plaintext in-place
cocoar-secrets decrypt \
  --file appsettings.json \
  --path "Database:ConnectionString" \
  --cert cert.pfx \
  --replace
```

## generate-cert

Generate a self-signed certificate for encryption.

```shell
cocoar-secrets generate-cert --output <path> [options]
```

| Option | Alias | Type | Default | Description |
|---|---|---|---|---|
| `--output` | `-o` | string | *required* | Output path for certificate file(s) |
| `--password` | `-pwd` | string | — | Password for PFX file (omit for password-less) |
| `--format` | `-fmt` | string | `"auto"` | Output format: `pfx`, `pem`, or `auto` (infer from extension) |
| `--subject` | `-s` | string | `"CN=Cocoar Secrets"` | Certificate subject |
| `--valid-years` | | int | `1` | Validity period in years |
| `--key-size` | | int | `2048` | RSA key size (2048, 3072, or 4096) |
| `--overwrite` | | bool | `false` | Overwrite existing file without prompt |

**Examples:**

```shell
# Generate a password-less PFX certificate
cocoar-secrets generate-cert --output certs/config.pfx

# Generate a PEM certificate with custom subject
cocoar-secrets generate-cert \
  --output certs/config.pem \
  --subject "CN=My App Secrets" \
  --valid-years 5 \
  --key-size 4096
```

::: tip
Password-less certificates are recommended. Protect them with file permissions instead:
- **Windows:** `icacls cert.pfx /inheritance:r /grant:r "YourUser:(R)"`
- **Linux/macOS:** `chmod 600 cert.pfx`
:::

## convert-cert

Convert a certificate between PFX and PEM formats.

```shell
cocoar-secrets convert-cert --input <path> --output <path> [options]
```

| Option | Alias | Type | Default | Description |
|---|---|---|---|---|
| `--input` | `-i` | string | *required* | Input certificate file |
| `--output` | `-o` | string | *required* | Output certificate file |
| `--input-password` | `--ipass` | string | — | Password for input PFX file |
| `--output-password` | `--opass` | string | — | Password for output PFX file (omit for password-less) |
| `--format` | `-f` | string | `"auto"` | Output format: `pfx`, `pem`, or `auto` |
| `--overwrite` | | bool | `false` | Overwrite existing output file(s) |

**Examples:**

```shell
# Convert password-protected PFX to password-less PFX
cocoar-secrets convert-cert \
  --input cert.pfx \
  --ipass "OldPassword" \
  --output cert-nopwd.pfx

# Convert PFX to PEM
cocoar-secrets convert-cert \
  --input cert.pfx \
  --output cert.pem
```

## cert-info

Display detailed information about a certificate.

```shell
cocoar-secrets cert-info --input <path> [options]
```

| Option | Alias | Type | Default | Description |
|---|---|---|---|---|
| `--input` | `-i` | string | *required* | Certificate file path (PFX or PEM) |
| `--password` | `-pwd` | string | — | Certificate password (if password-protected) |

**Output includes:**
- Certificate details: Subject, Issuer, Serial Number, Thumbprint
- Validity: Not Before, Not After, status (Valid/Expired/Not yet valid)
- Key information: Algorithm, Key Size, Private Key presence, Password protection
- File information: Size, format, timestamps

**Example:**

```shell
cocoar-secrets cert-info --input certs/config.pfx
```

## Exit Codes

All commands use consistent exit codes:

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Argument error |
| 2 | I/O error (file not found, permission denied) |
| 3 | Cryptographic error (wrong certificate, corrupt data) |
| 4 | General error |

## Encryption Details

All commands use the same encryption scheme:

| Purpose | Algorithm |
|---|---|
| Key wrapping | RSA-OAEP-SHA256 |
| Data encryption | AES-256-GCM |

The encrypted value is stored as a JSON envelope with fields: `type`, `version`, `kid`, `alg`, `wk`, `walg`, `iv`, `ct`, `tag`.
