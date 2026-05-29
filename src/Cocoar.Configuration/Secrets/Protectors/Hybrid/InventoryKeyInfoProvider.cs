using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Builds the current encryption public key from a <see cref="CertificateInventory"/> for a single
/// configured kid. Exports only public-key bytes (SPKI), base64url-encoded without padding — the
/// same codec the <c>cocoar.secret</c> envelope wire format uses.
/// </summary>
internal sealed class InventoryKeyInfoProvider : ISecretEncryptionKeyInfoProvider
{
    private readonly CertificateInventory _inventory;
    private readonly string _kid;

    public InventoryKeyInfoProvider(CertificateInventory inventory, string kid)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _kid = kid ?? throw new ArgumentNullException(nameof(kid));
    }

    public SecretEncryptionPublicKey? TryGetCurrentKey()
    {
        var spki = _inventory.TryExportPreferredPublicKey();
        if (spki is null)
            return null;

        return new SecretEncryptionPublicKey
        {
            Kid = _kid,
            PublicKey = ToBase64Url(spki),
        };
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
