# Intelligent Certificate Caching

## Overview

Folder-based certificate mode (UseCertificatesFromFolder) provides intelligent caching and automatic certificate rotation for zero-downtime key management. This document covers when and how to use this feature.

**Key Benefits:**
- **Time-Limited Memory Exposure**: Private keys cached only for configurable duration (default: 30s)
- **Automatic Discovery**: FileSystemWatcher detects new/removed certificates without restart
- **Zero-Downtime Rotation**: Add new cert, old secrets still decrypt with old cert
- **Performance**: Two-level cache (envelope hash → cert path → loaded cert) eliminates redundant I/O

---

## Basic Usage

```csharp
var manager = new ConfigManager(
    new[] { rule },
    setup => new[]
    {
        setup.Secrets()
            .UseCertificatesFromFolder(@"C:\certs", 
                cacheDurationSeconds: 30)  // 30 seconds (default)
    });
```

**How It Works:**
1. **First decrypt**: Scans folder, tries certificates, caches which cert works for each secret
2. **Subsequent decrypt**: Uses cached certificate (no folder scan, no file I/O)
3. **After TTL expires**: Reloads certificate from disk, knows exact file to load
4. **New cert added**: FileSystemWatcher detects it, adds to inventory automatically
5. **Old cert removed**: Cache evicted, secrets encrypted with it will fail to decrypt

---

## Cache Duration Guidelines

| Security Level | Cache Duration | Use Case |
|---------------|---------------|----------|
| **Critical** | 0 seconds | Payment data, passwords, PCI-DSS |
| **High** | 5-30 seconds | API keys, session tokens (default) |
| **Medium** | 60-300 seconds | Application secrets |
| **Low** | 300-3600+ seconds | Feature flags, non-sensitive config |

**Example:**

```csharp
// Critical secrets - no cache, load fresh every time
// Folder structure: C:\certs\pci\pci-data\*.pfx
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\pci", 
        cacheDurationSeconds: 0);

// Standard secrets - balanced 30-second cache
// Folder structure: C:\certs\api\api-keys\*.pfx
setup.Secrets()
    .UseCertificatesFromFolder(@"C:\certs\api", 
        cacheDurationSeconds: 30);
```

---

## Certificate Rotation

**Zero-downtime rotation process:**

1. Add new certificate to folder: cert-2024-12.pfx
2. FileSystemWatcher detects it (within ~1 second)
3. Old secrets still decrypt with cert-2024-11.pfx (backward compatibility)
4. New secrets automatically encrypted with newest cert
5. Re-encrypt old secrets in background (optional)
6. Remove old cert after all secrets migrated

**Important**: Keep old certificates in folder during transition period!

---

## When to Use Folder Mode vs File Mode

| Feature | UseCertificateFromFile | UseCertificatesFromFolder |
|---------|------------------------|--------------------------|
| **Certificate Loading** | Single file, stays in memory forever | Multiple files, cached with TTL |
| **Dynamic Discovery** | No (restart required) | Yes (FileSystemWatcher) |
| **Rotation Support** | Manual restart | Automatic |
| **Memory Exposure** | Continuous (keys always in memory) | Time-limited (configurable) |
| **Performance** | Fastest (no overhead) | Fast (cache eliminates most I/O) |
| **Best For** | Single cert, max performance, dev | Multiple certs, rotation, production |

---

## Best Practices

1. **Start Secure**: Begin with 0-second cache, increase only if performance testing proves necessary
2. **Separate by Classification**: Use different folders for different security levels
3. **Rotate Regularly**: Schedule certificate rotation every 90 days
4. **Keep Old Certs**: During rotation, keep old certs in folder for backward compatibility
5. **Monitor**: Log certificate loads and decrypt failures

---

## See Also

- [Secrets Library](README.md) - Runtime library and architecture
- [Secrets CLI](../Cocoar.Configuration.Secrets.Cli/README.md) - Command-line tools
