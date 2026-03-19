namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// A lease that provides access to a secret value.
/// When disposed, the secret value is securely zeroed from memory.
/// </summary>
/// <typeparam name="T">The type of the secret value.</typeparam>
public readonly struct SecretLease<T> : IDisposable
{
    /// <summary>
    /// Gets the secret value.
    /// </summary>
    public T Value { get; }

    private readonly Action? _onDispose;

    /// <summary>
    /// Creates a new secret lease with the specified value and optional cleanup action.
    /// </summary>
    /// <param name="value">The secret value.</param>
    /// <param name="onDispose">Action to invoke when the lease is disposed (typically zeroes memory).</param>
    public SecretLease(T value, Action? onDispose)
    {
        Value = value;
        _onDispose = onDispose;
    }

    /// <summary>
    /// Disposes the lease, invoking the cleanup action to zero sensitive data.
    /// </summary>
    public void Dispose()
    {
        try { _onDispose?.Invoke(); }
        catch { /* best-effort zeroization */ }
    }
}
