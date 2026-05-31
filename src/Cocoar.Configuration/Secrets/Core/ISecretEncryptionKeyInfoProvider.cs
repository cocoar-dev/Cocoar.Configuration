using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Internal capability composed beside a secrets protector. Exposes the current encryption public key,
/// re-read from the underlying certificate source on every call so rotation is reflected. Single-kid
/// mode composes one bound to that kid; folder / multi-tenant mode composes one that resolves the
/// preferred public key per kid (= per tenant) on demand.
/// </summary>
internal interface ISecretEncryptionKeyInfoProvider
{
    /// <summary>
    /// The current encryption public key for this provider's single, unambiguous kid, or
    /// <see langword="null"/> when none is available or this provider serves multiple kids
    /// (folder / multi-tenant mode).
    /// </summary>
    SecretEncryptionPublicKey? TryGetCurrentKey();

    /// <summary>
    /// The current encryption public key for the given <paramref name="kid"/> (in folder /
    /// multi-tenant mode <c>kid == tenant id</c>), or <see langword="null"/> if this provider does
    /// not serve that kid.
    /// </summary>
    SecretEncryptionPublicKey? TryGetKeyForKid(string kid);
}
