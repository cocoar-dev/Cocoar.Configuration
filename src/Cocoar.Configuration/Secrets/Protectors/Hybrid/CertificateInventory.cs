using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.Core;
using Cocoar.FileSystem;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

internal sealed class CertificateInventory : IDisposable
{
    private readonly string _folderPath;
    private readonly string _searchPattern;
    private readonly string? _kid;
    private readonly IConfigurationAccessor? _configAccessor;
    private readonly Func<CertificateContext, string[]>? _passwordProvider;
    private readonly TimeSpan _cacheDuration;
    private readonly IComparer<FileInfo>? _fileInfoComparer;
    private readonly int _includeSubdirectories;

    private readonly Dictionary<string, string> _envelopeHashToCertPath = new();
    private readonly Dictionary<string, CachedCert> _certCache = new();
    private readonly List<string> _sortedCertPaths = new();

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ResilientFileSystemMonitor _monitor;

    public CertificateInventory(string folderPath, string searchPattern, string? kid, IConfigurationAccessor? configAccessor, Func<CertificateContext, string[]>? passwordProvider, int cacheDurationSeconds = 30, IComparer<FileInfo>? fileInfoComparer = null, int includeSubdirectories = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        // Resolve relative paths relative to the application directory, not the working directory
        _folderPath = Path.IsPathRooted(folderPath)
            ? Path.GetFullPath(folderPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folderPath));
        _searchPattern = searchPattern;
        _kid = kid;
        _configAccessor = configAccessor;
        _passwordProvider = passwordProvider;
        _cacheDuration = TimeSpan.FromSeconds(cacheDurationSeconds);
        _fileInfoComparer = fileInfoComparer;
        _includeSubdirectories = includeSubdirectories;

        RefreshInventory();

        // Build file watcher with v2.1.0 features
        var monitorBuilder = ResilientFileSystemMonitor
            .Watch(_folderPath);

        // Apply file filters - v2.1.0 supports multiple patterns via .WithFilter()
        if (_searchPattern.Contains(';'))
        {
            // Multiple patterns (semicolon-separated) - add each separately
            var patterns = _searchPattern.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pattern in patterns)
            {
                monitorBuilder = monitorBuilder.WithFilter(pattern.Trim());
            }
        }
        else if (_searchPattern == "*" || _searchPattern == "*.*")
        {
            // Auto-discover all supported formats
            monitorBuilder = monitorBuilder
                .WithFilter("*.pfx", "*.p12", "*.pem", "*.crt", "*.cer", "*.key");
        }
        else
        {
            // Single pattern
            monitorBuilder = monitorBuilder.WithFilter(_searchPattern);
        }

        monitorBuilder = monitorBuilder.WithDebounce(100);

        // Apply subdirectory monitoring based on depth parameter
        // -1 = unlimited depth (recursive)
        // 0 = no subdirectories (flat, default)
        // N = max depth of N levels
        if (includeSubdirectories != 0)
        {
            monitorBuilder = monitorBuilder.IncludeSubdirectories(includeSubdirectories);
        }

        _monitor = monitorBuilder
            .OnCreated(OnFileSystemChanged)
            .OnDeleted(OnFileSystemDeleted)
            .OnChanged(OnFileSystemChanged)
            .OnRenamed(OnFileSystemRenamed)
            .Build();
    }

    public bool TryDecrypt(HybridEnvelope envelope, out byte[] plaintext)
    {
        var envelopeHash = ComputeEnvelopeHash(envelope);

        _lock.EnterReadLock();
        try
        {
            if (_envelopeHashToCertPath.TryGetValue(envelopeHash, out var knownCertPath))
            {
                if (TryDecryptWithCert(knownCertPath, envelope, out plaintext))
                    return true;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        _lock.EnterWriteLock();
        try
        {
            _envelopeHashToCertPath.Remove(envelopeHash);

            foreach (var certPath in _sortedCertPaths)
            {
                if (TryDecryptWithCert(certPath, envelope, out plaintext))
                {
                    _envelopeHashToCertPath[envelopeHash] = certPath;
                    return true;
                }
            }

            plaintext = Array.Empty<byte>();
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool HasCertificatesFor(string kid)
    {
        var kidPath = Path.GetFullPath(Path.Combine(_folderPath, kid));

        _lock.EnterReadLock();
        try
        {
            // Check if any certificates exist in the kid-specific folder
            // Add directory separator to ensure we match the folder, not a prefix
            var kidPathWithSep = kidPath + Path.DirectorySeparatorChar;
            return _sortedCertPaths.Any(path => path.StartsWith(kidPathWithSep, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(Path.GetDirectoryName(path), kidPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool TryDecryptWithKid(HybridEnvelope envelope, string kid, out byte[] plaintext)
    {
        var kidPath = Path.GetFullPath(Path.Combine(_folderPath, kid));
        var kidPathWithSep = kidPath + Path.DirectorySeparatorChar;
        var envelopeHash = ComputeEnvelopeHash(envelope);

        _lock.EnterReadLock();
        try
        {
            // Check cache first
            if (_envelopeHashToCertPath.TryGetValue(envelopeHash, out var knownCertPath))
            {
                var certDir = Path.GetDirectoryName(knownCertPath);
                if (certDir != null && (knownCertPath.StartsWith(kidPathWithSep, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(certDir, kidPath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (TryDecryptWithCert(knownCertPath, envelope, out plaintext))
                        return true;
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        _lock.EnterWriteLock();
        try
        {
            _envelopeHashToCertPath.Remove(envelopeHash);

            // Only try certificates from the kid-specific folder
            foreach (var certPath in _sortedCertPaths)
            {
                var certDir = Path.GetDirectoryName(certPath);
                if (certDir == null || !(certPath.StartsWith(kidPathWithSep, StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(certDir, kidPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (TryDecryptWithCert(certPath, envelope, out plaintext))
                {
                    _envelopeHashToCertPath[envelopeHash] = certPath;
                    return true;
                }
            }

            plaintext = Array.Empty<byte>();
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Exports the SubjectPublicKeyInfo (DER) of the certificate the decryption engine prefers
    /// (the first in the current ordering) — for publishing as the encryption public key. Returns
    /// only public-key bytes; the <see cref="X509Certificate2"/> is never exposed. Returns
    /// <see langword="null"/> when no usable RSA certificate is present. Runs under the write lock
    /// because <see cref="GetOrLoadCertificate"/> mutates the cache.
    /// </summary>
    internal byte[]? TryExportPreferredPublicKey()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var certPath in _sortedCertPaths)
            {
                X509Certificate2 cert;
                try
                {
                    cert = GetOrLoadCertificate(certPath);
                }
                catch (Exception ex) when (
                    ex is CryptographicException or IOException or UnauthorizedAccessException
                       or InvalidOperationException or NotSupportedException)
                {
                    // A cert that can't be loaded right now — e.g. a transient file race during
                    // rotation (delete+recreate / locked / partially written) or a non-usable file —
                    // is skipped so publishing degrades gracefully (empty result) instead of a 500.
                    continue;
                }

                using var rsa = cert.GetRSAPublicKey();
                if (rsa is null)
                    continue;

                return rsa.ExportSubjectPublicKeyInfo();
            }

            return null;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private bool TryDecryptWithCert(string certPath, HybridEnvelope envelope, out byte[] plaintext)
    {
        try
        {
            var cert = GetOrLoadCertificate(certPath);
            var rsa = cert.GetRSAPrivateKey() ?? throw new CryptographicException($"Certificate has no RSA private key: {certPath}");

            if (!string.Equals(envelope.WrappingAlgorithm, "RSA-OAEP-256", StringComparison.OrdinalIgnoreCase))
            {
                plaintext = Array.Empty<byte>();
                return false;
            }

            Span<byte> dek = stackalloc byte[32];
            try
            {
                var unwrappedKey = rsa.Decrypt(envelope.WrappedKey, RSAEncryptionPadding.OaepSHA256);
                unwrappedKey.AsSpan().CopyTo(dek);

                plaintext = new byte[envelope.Ciphertext.Length];
                using (var aes = new AesGcm(dek, envelope.Tag.Length))
                {
                    aes.Decrypt(envelope.Iv, envelope.Ciphertext, envelope.Tag, plaintext, associatedData: null);
                }

                Array.Clear(unwrappedKey, 0, unwrappedKey.Length);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dek);
            }
        }
        catch (CryptographicException)
        {
            plaintext = Array.Empty<byte>();
            return false;
        }
    }

    private X509Certificate2 GetOrLoadCertificate(string certPath)
    {
        var now = DateTime.UtcNow;

        if (_certCache.TryGetValue(certPath, out var cached))
        {
            if (now < cached.ExpiresAt)
            {
                cached.ExpiresAt = now.Add(_cacheDuration);
                return cached.Certificate;
            }

            cached.Certificate.Dispose();
            _certCache.Remove(certPath);
        }

        X509Certificate2? cert = null;

        if (_passwordProvider != null)
        {
            var context = new CertificateContext
            {
                Config = _configAccessor!,
                FilePath = certPath,
                Kid = _kid
            };
            var passwords = _passwordProvider(context);
            foreach (var password in passwords)
            {
                try
                {
                    cert = Helpers.CertificateHelper.LoadFromFile(certPath, password);
                    break;
                }
                catch (CryptographicException)
                {
                    // Try next password
                }
            }
        }

        cert ??= Helpers.CertificateHelper.LoadFromFile(certPath, null);

        _certCache[certPath] = new CachedCert
        {
            Certificate = cert,
            ExpiresAt = now.Add(_cacheDuration)
        };

        return cert;
    }

    private static string ComputeEnvelopeHash(HybridEnvelope envelope)
    {
        using var sha = SHA256.Create();

        var hashInput = new byte[envelope.Ciphertext.Length + envelope.Iv.Length + envelope.WrappedKey.Length + envelope.Tag.Length];
        envelope.Ciphertext.CopyTo(hashInput, 0);
        envelope.Iv.CopyTo(hashInput, envelope.Ciphertext.Length);
        envelope.WrappedKey.CopyTo(hashInput, envelope.Ciphertext.Length + envelope.Iv.Length);
        envelope.Tag.CopyTo(hashInput, envelope.Ciphertext.Length + envelope.Iv.Length + envelope.WrappedKey.Length);

        var hash = SHA256.HashData(hashInput);
        return Convert.ToBase64String(hash);
    }

    private void RefreshInventory()
    {
        _lock.EnterWriteLock();
        try
        {
            _sortedCertPaths.Clear();

            if (!Directory.Exists(_folderPath))
                return;

            // Discover certificate files using FileSearcher with MaxDepth support
            var certPaths = DiscoverCertificates();

            if (_fileInfoComparer != null)
            {
                var fileInfos = certPaths.Select(p => new FileInfo(p)).ToList();
                fileInfos.Sort(_fileInfoComparer);
                _sortedCertPaths.AddRange(fileInfos.Select(fi => fi.FullName));
            }
            else
            {
                _sortedCertPaths.AddRange(certPaths.OrderByDescending(p => p));
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private HashSet<string> DiscoverCertificates()
    {
        var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Determine patterns to search
        var patterns = _searchPattern.Contains(';')
            ? _searchPattern.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray()
            : _searchPattern == "*" || _searchPattern == "*.*"
                ? new[] { "*.pfx", "*.p12", "*.pem", "*.crt", "*.cer" }
                : new[] { _searchPattern };

        // Use FileSearcher with MaxDepth support from Cocoar.FileSystem
        foreach (var pattern in patterns)
        {
            var searcher = FileSearcher.Search(_folderPath, pattern);

            // Apply MaxDepth if subdirectories are requested
            // -1 = unlimited, 0 = flat (no subdirs), N = max N levels deep
            if (_includeSubdirectories != 0)
            {
                searcher = _includeSubdirectories == -1
                    ? searcher.WithMaxDepth(int.MaxValue)  // Unlimited
                    : searcher.WithMaxDepth(_includeSubdirectories);
            }

            foreach (var file in searcher)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // Skip standalone .key files (they're paired with .crt/.pem/.cer files)
                if (ext == ".key")
                    continue;

                // For PEM/CRT/CER, only include if matching .key file exists
                if (ext is ".pem" or ".crt" or ".cer")
                {
                    var keyFile = Path.ChangeExtension(file, ".key");
                    if (!File.Exists(keyFile))
                        continue; // Skip - missing required private key
                }

                discoveredPaths.Add(file);
            }
        }

        return discoveredPaths;
    }

    private void OnFileSystemChanged(object? sender, FileSystemEventArgs e)
    {
        // Refresh on file create, change, or rename
        RefreshInventory();
    }

    private void OnFileSystemRenamed(object? sender, RenamedEventArgs e)
    {
        // Folder or file rename - refresh inventory to discover new paths
        RefreshInventory();
    }

    private void OnFileSystemDeleted(object? sender, FileSystemEventArgs e)
    {
        RefreshInventory();

        _lock.EnterWriteLock();
        try
        {
            var fullPath = Path.GetFullPath(e.FullPath);
            if (_certCache.TryGetValue(fullPath, out var cached))
            {
                cached.Certificate.Dispose();
                _certCache.Remove(fullPath);
            }

            var keysToRemove = _envelopeHashToCertPath
                .Where(kvp => kvp.Value == fullPath)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
                _envelopeHashToCertPath.Remove(key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _monitor?.Dispose();

        _lock.EnterWriteLock();
        try
        {
            foreach (var cached in _certCache.Values)
            {
                cached.Certificate.Dispose();
            }
            _certCache.Clear();
            _envelopeHashToCertPath.Clear();
            _sortedCertPaths.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        _lock.Dispose();
    }

    private sealed class CachedCert
    {
        public required X509Certificate2 Certificate { get; init; }
        public DateTime ExpiresAt { get; set; }
    }
}
