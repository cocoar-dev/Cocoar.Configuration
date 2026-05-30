using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Internal capability composed beside a secrets protector. Exposes the current encryption public key
/// for the protector's kid, re-read from the underlying certificate source on every call so rotation
/// is reflected. Composed only where a single publishable encryption kid is unambiguous.
/// </summary>
internal interface ISecretEncryptionKeyInfoProvider
{
    /// <summary>The current encryption public key, or <see langword="null"/> when none is available.</summary>
    SecretEncryptionPublicKey? TryGetCurrentKey();
}
