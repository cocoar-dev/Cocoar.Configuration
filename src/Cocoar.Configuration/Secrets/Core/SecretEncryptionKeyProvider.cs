using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Public-facing <see cref="ISecretEncryptionKeyProvider"/>. Resolves the composed
/// <see cref="ISecretEncryptionKeyInfoProvider"/> capabilities lazily on every call — so certificate
/// rotation is reflected and no stale snapshot is held — and returns exactly one current public key
/// (the single-tenant key, or the requested tenant's key; never a list).
/// </summary>
internal sealed class SecretEncryptionKeyProvider : ISecretEncryptionKeyProvider
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretEncryptionKeyProvider(ConfigManagerCapabilityScope scope)
        => _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public SecretEncryptionPublicKey? GetCurrentKey()
    {
        var composition = _scope.Owner.GetComposition();
        var infoProviders = composition?.GetAll<ISecretEncryptionKeyInfoProvider>();
        if (infoProviders is null)
            return null;

        foreach (var info in infoProviders)
        {
            var key = info.TryGetCurrentKey();
            if (key is not null)
                return key;
        }

        return null;
    }

    public SecretEncryptionPublicKey? GetCurrentKeyForTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        var composition = _scope.Owner.GetComposition();
        var infoProviders = composition?.GetAll<ISecretEncryptionKeyInfoProvider>();
        if (infoProviders is null)
            return null;

        // Exactly one tenant is queried; return that tenant's single current key and never
        // enumerate or expose any other tenant's key.
        foreach (var info in infoProviders)
        {
            var key = info.TryGetKeyForKid(tenantId);
            if (key is not null)
                return key;
        }

        return null;
    }
}
