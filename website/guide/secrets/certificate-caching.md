# Certificate Caching

<Badge type="info" text="ADV" />

Folder-based certificate mode (`UseCertificatesFromFolder`) provides intelligent caching and automatic certificate rotation for zero-downtime key management.

**Key benefits:**

- **Time-limited memory exposure** — private keys cached only for a configurable duration (default: 30s)
- **Automatic discovery** — `FileSystemWatcher` detects new/removed certificates without restart
- **Zero-downtime rotation** — add a new cert while old secrets still decrypt with the old one
- **Performance** — two-level cache (envelope hash to cert path to loaded cert) eliminates redundant I/O

## Basic Usage

```csharp
var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [ /* ... */ ])
    .UseSecretsSetup(secrets => secrets
        .UseCertificatesFromFolder("certs/",
            cacheDurationSeconds: 30)));  // 30 seconds (default)
```

**How it works:**

1. **First decrypt** — scans folder, tries certificates, caches which cert works for each secret
2. **Subsequent decrypts** — uses cached certificate (no folder scan, no file I/O)
3. **After TTL expires** — reloads certificate from disk, knows exact file to load
4. **New cert added** — `FileSystemWatcher` detects it, adds to inventory automatically
5. **Old cert removed** — cache evicted, secrets encrypted with it will fail to decrypt

## Cache Duration Guidelines

| Security Level | Cache Duration | Use Case |
|---|---|---|
| **Critical** | 0 seconds | Payment data, passwords, PCI-DSS |
| **High** | 5-30 seconds | API keys, session tokens (default) |
| **Medium** | 60-300 seconds | Application secrets |
| **Low** | 300-3600+ seconds | Feature flags, non-sensitive config |

::: tip Start Secure
Begin with `cacheDurationSeconds: 0` and increase only if performance testing proves it necessary. A zero-duration cache still avoids redundant folder scans — it just reloads the certificate file on every decrypt.
:::

```csharp
// Critical secrets — no cache, load fresh every time
.UseSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder("certs/pci/",
        cacheDurationSeconds: 0))

// Standard secrets — balanced 30-second cache
.UseSecretsSetup(secrets => secrets
    .UseCertificatesFromFolder("certs/api/",
        cacheDurationSeconds: 30))
```

## Certificate Rotation

Zero-downtime rotation process:

1. Add new certificate to folder (e.g., `cert-2024-12.pfx`)
2. `FileSystemWatcher` detects it (within ~1 second)
3. Old secrets still decrypt with `cert-2024-11.pfx` (backward compatibility)
4. New secrets automatically encrypted with newest cert
5. Re-encrypt old secrets in background (optional)
6. Remove old cert after all secrets are migrated

::: warning Keep Old Certificates During Transition
Do not remove old certificates from the folder until all secrets encrypted with them have been re-encrypted with the new certificate. Removing a certificate makes any secrets encrypted with it permanently undecryptable.
:::

## Folder Mode vs File Mode

| Feature | `UseCertificateFromFile` | `UseCertificatesFromFolder` |
|---|---|---|
| **Certificate loading** | Single file, stays in memory forever | Multiple files, cached with TTL |
| **Dynamic discovery** | No (restart required) | Yes (`FileSystemWatcher`) |
| **Rotation support** | Manual restart | Automatic |
| **Memory exposure** | Continuous (keys always in memory) | Time-limited (configurable) |
| **Performance** | Fastest (no overhead) | Fast (cache eliminates most I/O) |
| **Best for** | Single cert, max performance, dev | Multiple certs, rotation, production |

## Best Practices

1. **Separate by classification** — use different folders for different security levels
2. **Rotate regularly** — schedule certificate rotation every 90 days
3. **Keep old certs** — during rotation, keep old certs in folder for backward compatibility
4. **Monitor** — log certificate loads and decrypt failures

## See Also

- [Encryption Setup](/guide/secrets/encryption-setup) — certificate configuration and encrypted envelope format
- [Security Model](/guide/secrets/security-model) — full rotation workflow and threat model
- [CLI Tools](/guide/secrets/cli) — encrypt, decrypt, and manage certificates from the command line
