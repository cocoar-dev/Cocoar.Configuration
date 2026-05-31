using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Publishes the current encryption public key per kid for folder / multi-tenant mode, where each kid
/// is a subfolder (kid = tenant). Resolves the preferred (newest per the configured comparer) cert in
/// the requested kid's subfolder on demand, so rotation and tenant add/remove are reflected live.
/// There is no single "current" key — callers ask per kid (= per tenant).
/// </summary>
internal sealed class FolderKeyInfoProvider : ISecretEncryptionKeyInfoProvider
{
    private readonly CertificateInventory _inventory;

    public FolderKeyInfoProvider(CertificateInventory inventory)
        => _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

    // Folder mode serves many kids; there is no single unambiguous "current" kid to publish.
    public SecretEncryptionPublicKey? TryGetCurrentKey() => null;

    public SecretEncryptionPublicKey? TryGetKeyForKid(string kid)
    {
        if (string.IsNullOrWhiteSpace(kid))
            return null;

        var spki = _inventory.TryExportPreferredPublicKey(kid);
        if (spki is null)
            return null;

        return new SecretEncryptionPublicKey
        {
            Kid = kid,
            PublicKey = SpkiEncoding.ToBase64Url(spki),
        };
    }
}
