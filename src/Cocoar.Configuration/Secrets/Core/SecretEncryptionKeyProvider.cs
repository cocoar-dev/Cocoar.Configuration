using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Public-facing <see cref="ISecretEncryptionKeyProvider"/>. Resolves the composed
/// <see cref="ISecretEncryptionKeyInfoProvider"/> capabilities lazily on every call — so certificate
/// rotation is reflected and no stale snapshot is held — and aggregates one current key per kid.
/// </summary>
internal sealed class SecretEncryptionKeyProvider : ISecretEncryptionKeyProvider
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretEncryptionKeyProvider(ConfigManagerCapabilityScope scope)
        => _scope = scope ?? throw new ArgumentNullException(nameof(scope));

    public IReadOnlyList<SecretEncryptionPublicKey> GetCurrentKeys()
    {
        var composition = _scope.Owner.GetComposition();
        var infoProviders = composition?.GetAll<ISecretEncryptionKeyInfoProvider>();
        if (infoProviders is null || infoProviders.Count == 0)
            return Array.Empty<SecretEncryptionPublicKey>();

        // One key per kid. On a kid collision the VALUE is last-writer-wins (matching the decrypt
        // resolver's recency preference); the emitted list POSITION is first-appearance. Collisions
        // cannot occur in the current single-kid publishing path — revisit ordering for multi-kid.
        var byKid = new Dictionary<string, SecretEncryptionPublicKey>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var info in infoProviders)
        {
            var key = info.TryGetCurrentKey();
            if (key is null)
                continue;

            if (!byKid.ContainsKey(key.Kid))
                order.Add(key.Kid);
            byKid[key.Kid] = key;
        }

        if (order.Count == 0)
            return Array.Empty<SecretEncryptionPublicKey>();

        var result = new List<SecretEncryptionPublicKey>(order.Count);
        foreach (var kid in order)
            result.Add(byKid[kid]);
        return result;
    }

    public SecretEncryptionPublicKey? GetCurrentKey(string kid)
    {
        if (string.IsNullOrEmpty(kid))
            return null;

        foreach (var key in GetCurrentKeys())
        {
            if (string.Equals(key.Kid, kid, StringComparison.Ordinal))
                return key;
        }

        return null;
    }
}
