# Working with Certificates

This guide explains how Cocoar.Configuration uses X.509 certificates, why we require them to be password-less, and how to manage them securely. These principles apply everywhere in the library where certificates are used.

## Why Password-Less?

A password-protected certificate seems more secure — but it creates a circular problem: **where do you store the password?**

- If you hardcode it → it's in your source code
- If you put it in a config file → it's plaintext on disk
- If you encrypt it → you need another key to decrypt it

The password becomes another secret that needs managing. Every tool in the chain needs it — your app, your deployment scripts, your CI/CD pipeline. Each handoff is a potential leak.

**Password-less certificates eliminate this problem entirely.** Instead of protecting the certificate with a password (something you know), you protect it with file system permissions (something the OS enforces).

This is not unusual — it's the industry standard:

| Software | Default Behavior |
|----------|-----------------|
| **nginx** | Expects password-less PEM/key files |
| **HAProxy** | Expects password-less PEM bundles |
| **Kestrel** | Supports password-less PFX for HTTPS |
| **Docker/Kubernetes** | Mounts certs as files with permission control |
| **Let's Encrypt** | Generates password-less PEM files |

## How to Protect Certificates

Without a password, the certificate file's security depends entirely on file system permissions. This is actually **stronger** than a password — the OS enforces it at every access, not just at load time.

### Linux / macOS

```shell
# Only the app user can read the certificate
chmod 600 certs/secrets.pfx
chown app-user:app-group certs/secrets.pfx

# Verify
ls -la certs/secrets.pfx
# -rw------- 1 app-user app-group 2048 Mar 19 10:00 secrets.pfx
```

### Windows

```powershell
# Remove inherited permissions + grant read-only to app user
icacls certs\secrets.pfx /inheritance:r /grant:r "AppPoolUser:(R)"
```

### Docker / Kubernetes

Mount certificates as read-only volumes with restricted permissions:

```yaml
# Docker Compose
volumes:
  - ./certs:/app/certs:ro

# Kubernetes Secret
apiVersion: v1
kind: Secret
metadata:
  name: config-certs
type: kubernetes.io/tls
data:
  tls.crt: <base64>
  tls.key: <base64>
```

## Why File-Based?

Cocoar uses **file-based certificates** exclusively for secret encryption. This is a deliberate design choice:

- **Cache & Dispose** — Certificates are loaded on demand, cached briefly (default 30 seconds), then disposed. The private key lives in memory only when actively used for decryption.
- **Automatic Rotation** — Drop a new certificate into the folder. The file watcher detects it automatically. Old secrets still decrypt with the old certificate. No restart needed.
- **Cross-Platform** — Works identically on Windows, Linux, macOS, and in containers.
- **No Store Dependency** — The Windows Certificate Store, Linux keyrings, and macOS Keychain all behave differently. Files with permissions work everywhere.

:::info What about the OS Certificate Store?
The Windows Certificate Store offers hardware-backed key protection (TPM), but it doesn't support Cocoar's cache/dispose cycle or folder-based rotation. On Linux, .NET's `X509Store` is just a file directory under `~/.dotnet/` — no security benefit over direct file access.

If you need OS Store certificates for **HTTP client authentication** (mutual TLS), load the certificate yourself and pass it via `HttpMessageHandler`. See [HTTP Provider Authentication](/guide/providers/http-polling#authentication).
:::

### Password-Less Files

Use password-less certificate files. The private key is loaded into managed memory briefly — don't add a password that also enters managed memory:

```csharp
var cert = new X509Certificate2("certs/secrets.pfx");
```

:::warning
`new X509Certificate2("cert.pfx", "password")` loads both the certificate AND the password into managed memory as strings. The password cannot be zeroed because .NET strings are immutable. Avoid this pattern — use `cocoar-secrets convert-cert` to remove passwords.
:::

## Certificate Formats

| Format | Extensions | Notes |
|--------|-----------|-------|
| **PKCS#12** | `.pfx`, `.p12` | Contains certificate + private key in one file |
| **PEM** | `.pem`, `.crt` + `.key` | Certificate and key as separate text files |

Convert between formats with the CLI:

```shell
# PFX to PEM
cocoar-secrets convert-cert --input cert.pfx --output cert.pem

# Password-protected to password-less
cocoar-secrets convert-cert --input protected.pfx --ipass "OldPassword" --output cert.pfx
```

## Rotation

Certificate rotation follows the same principle as key rotation — overlap old and new during transition:

1. **Generate** a new certificate
2. **Deploy** alongside the old one (both accepted)
3. **Re-encrypt** secrets with the new certificate's public key
4. **Remove** the old certificate after all secrets are re-encrypted

For automated rotation with certificate folders, see [Certificate Caching](/guide/secrets/certificate-caching).

## Summary

| Do | Don't |
|----|-------|
| Use password-less certificate files | Use password-protected certificates |
| Protect with file permissions (`chmod 600`, ACLs) | Store passwords in config files or code |
| Use folder-based certs for automatic rotation | Manually restart on certificate change |
| Rotate certificates periodically | Use the same certificate forever |
| Use `cocoar-secrets convert-cert` to remove passwords | Load PFX with password in code |
