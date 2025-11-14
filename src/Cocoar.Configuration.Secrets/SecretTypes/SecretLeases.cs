namespace Cocoar.Configuration.Secrets.SecretTypes;

public readonly struct SecretLease<T> : IDisposable
{
    public T Value { get; }
    private readonly Action? _onDispose;

    public SecretLease(T value, Action? onDispose)
    {
        Value = value;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        try { _onDispose?.Invoke(); }
        catch { /* best-effort zeroization */ }
    }
}
