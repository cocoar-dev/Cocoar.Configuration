using System.Security.Cryptography;
using Cocoar.Configuration.Helper;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Rules;

/// <summary>
/// Manages byte caching and transformation deduplication for configuration rules.
/// Handles secure byte storage, hash-based change detection, and transform key tracking.
/// </summary>
internal sealed class TransformCache : IDisposable
{
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif
    private SecureBytes? _cachedBytes;
    private string? _lastTransformKey;
    private string? _lastSelectionHash;
    private bool _dirty;
    private bool _dirtyFromTransformChange;

    /// <summary>
    /// Gets whether the cache is dirty and needs refresh.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Gets the last computed selection hash for change detection.
    /// </summary>
    public string? LastSelectionHash
    {
        get => _lastSelectionHash;
        set => _lastSelectionHash = value;
    }

    /// <summary>
    /// Checks if the cache has valid bytes and is not dirty.
    /// </summary>
    public bool HasValidCache { get { lock (_lock) return !_dirty && _cachedBytes is not null; } }

    /// <summary>
    /// Checks if cache is dirty but has bytes that can be reused (no transform change).
    /// </summary>
    public bool CanReuseWithoutFetch { get { lock (_lock) return _dirty && _cachedBytes is not null && !_dirtyFromTransformChange; } }

    /// <summary>
    /// Gets the cached bytes as read-only memory. Returns empty if no cache.
    /// </summary>
    public ReadOnlyMemory<byte> GetCachedBytes()
    {
        lock (_lock) return _cachedBytes?.AsReadOnlyMemory() ?? ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// Updates the transform key and marks cache dirty if changed.
    /// </summary>
    public void UpdateTransformKey(string newTransformKey)
    {
        lock (_lock)
        {
            if (_lastTransformKey == newTransformKey)
            {
                return;
            }

            _dirty = true;
            _dirtyFromTransformChange = true;
            _lastTransformKey = newTransformKey;
        }
    }

    /// <summary>
    /// Stores transformed bytes in the cache and clears dirty flags.
    /// </summary>
    public void StoreTransformedBytes(byte[] transformedBytes)
    {
        lock (_lock)
        {
            if (_cachedBytes is null)
            {
                _cachedBytes = SecureBytes.From(transformedBytes);
            }
            else
            {
                _cachedBytes.Replace(transformedBytes);
            }

            _dirty = false;
            _dirtyFromTransformChange = false;
        }
    }

    /// <summary>
    /// Processes provider change bytes: transforms, hashes, and caches if changed.
    /// Returns true if the data actually changed (hash differs).
    /// </summary>
    public bool ProcessProviderChange(byte[] rawBytes, string? selectPath, string? mountPath)
    {
        try
        {
            var transformed = JsonTransform.SelectAndMount(rawBytes, selectPath, mountPath);
            var hash = ComputeSelectionHash(transformed);

            lock (_lock)
            {
                if (_lastSelectionHash is not null &&
                    string.Equals(hash, _lastSelectionHash, StringComparison.Ordinal))
                {
                    return false; // No change
                }

                _lastSelectionHash = hash;

                if (_cachedBytes is null)
                {
                    _cachedBytes = SecureBytes.From(transformed);
                }
                else
                {
                    _cachedBytes.Replace(transformed);
                }
                _dirty = true;
                _dirtyFromTransformChange = false;

                return true; // Data changed
            }
        }
        catch
        {
            return false; // Ignore transform errors in change handler
        }
    }

    /// <summary>
    /// Marks the cache as clean (used after successful reuse without fetch).
    /// </summary>
    public void MarkClean()
    {
        lock (_lock) _dirty = false;
    }

    /// <summary>
    /// Invalidates the entire cache (used when provider changes).
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _lastSelectionHash = null;
            _dirty = true;
            _cachedBytes?.Dispose();
            _cachedBytes = null;
        }
    }

    /// <summary>
    /// Clears the cached bytes (zeros them) without disposing the SecureBytes object.
    /// Used to zero plaintext before replacing with encrypted bytes.
    /// </summary>
    public void ClearCachedBytes()
    {
        lock (_lock) _cachedBytes?.Clear();
    }

    /// <summary>
    /// Updates the cached bytes with encrypted/preprocessed bytes.
    /// This prevents plaintext secrets from lingering in memory.
    /// </summary>
    public void UpdateCachedBytes(byte[] encryptedBytes)
    {
        lock (_lock)
        {
            if (_cachedBytes == null)
            {
                _cachedBytes = SecureBytes.From(encryptedBytes);
            }
            else
            {
                _cachedBytes.Replace(encryptedBytes);
            }
        }
    }

    private static string ComputeSelectionHash(byte[] transformedBytes)
    {
        try
        {
            var hash = SHA256.HashData(transformedBytes);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        _cachedBytes?.Dispose();
        _cachedBytes = null;
    }
}
