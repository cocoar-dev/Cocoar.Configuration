namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// Represents a secret value that can be opened to access its contents.
/// The secret value is protected and only revealed when <see cref="Open"/> is called.
/// </summary>
/// <typeparam name="T">The type of the secret value.</typeparam>
public interface ISecret<T> : IDisposable
{
    /// <summary>
    /// Opens the secret and returns a lease to access the decrypted value.
    /// The returned lease should be disposed when the value is no longer needed
    /// to allow secure cleanup of sensitive data in memory.
    /// </summary>
    /// <returns>A lease containing the decrypted secret value.</returns>
    SecretLease<T> Open();
}
