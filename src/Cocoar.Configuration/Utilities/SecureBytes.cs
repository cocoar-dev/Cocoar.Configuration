using System;
using System.Buffers;
using System.Security.Cryptography;

namespace Cocoar.Configuration.Utilities;

/// <summary>
/// A small utility wrapper that owns a byte buffer and guarantees zeroization
/// when replaced or disposed. Intended for holding sensitive payloads in-memory
/// for a bounded time. Not thread-safe.
/// </summary>
internal sealed class SecureBytes : IDisposable
{
    private byte[] _buffer;
    private int _length;
    private bool _disposed;
    private readonly bool _isPooled;

    private SecureBytes(int capacity, bool isPooled = false)
    {
        _isPooled = isPooled;
        
        if (isPooled)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        }
        else
        {
            // Use pinned allocation to prevent buffer movement in memory
            _buffer = GC.AllocateUninitializedArray<byte>(capacity, pinned: true);
        }
        
        _length = 0;
        if (!isPooled && capacity > 0)
        {
            GC.AddMemoryPressure(capacity);
        }
    }

    public static SecureBytes From(ReadOnlySpan<byte> data)
    {
        var s = new SecureBytes(data.Length, isPooled: false);
        data.CopyTo(s._buffer);
        s._length = data.Length;
        return s;
    }

    /// <summary>
    /// Creates a SecureBytes instance using ArrayPool for better performance with large buffers.
    /// Recommended for transient data that doesn't require pinned memory.
    /// </summary>
    public static SecureBytes FromPooled(ReadOnlySpan<byte> data)
    {
        var s = new SecureBytes(data.Length, isPooled: true);
        data.CopyTo(s._buffer.AsSpan(0, data.Length));
        s._length = data.Length;
        return s;
    }

    /// <summary>
    /// Replace the contents with <paramref name="data"/>. Zeroizes previous bytes
    /// even when the capacity matches and we reuse the same array.
    /// </summary>
    public void Replace(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        if (_buffer.Length >= data.Length)
        {
            // Always zero the ENTIRE buffer, not just _length, to prevent residual data leaks
            CryptographicOperations.ZeroMemory(_buffer);
            data.CopyTo(_buffer);
            _length = data.Length;
        }
        else
        {
            CryptographicOperations.ZeroMemory(_buffer);
            
            if (_isPooled)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(data.Length);
            }
            else
            {
                if (_buffer.Length > 0)
                {
                    GC.RemoveMemoryPressure(_buffer.Length);
                }
                _buffer = GC.AllocateUninitializedArray<byte>(data.Length, pinned: true);
                if (data.Length > 0)
                {
                    GC.AddMemoryPressure(data.Length);
                }
            }
            
            data.CopyTo(_buffer);
            _length = data.Length;
        }
    }

    /// <summary>
    /// Returns a read-only view of the current bytes. The view remains valid until
    /// the next Replace or Dispose call.
    /// </summary>
    public ReadOnlyMemory<byte> AsReadOnlyMemory()
    {
        ThrowIfDisposed();
        return new ReadOnlyMemory<byte>(_buffer, 0, _length);
    }

    /// <summary>
    /// Zeroizes the current contents but keeps capacity.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        // Zero entire buffer for security
        CryptographicOperations.ZeroMemory(_buffer);
        _length = 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            // Use cryptographically secure zeroization
            CryptographicOperations.ZeroMemory(_buffer);
            
            if (_isPooled)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
            else
            {
                if (_buffer.Length > 0)
                {
                    GC.RemoveMemoryPressure(_buffer.Length);
                }
            }
        }
        catch { }
        finally
        {
            _length = 0;
            _disposed = true;
        }
    }
}
