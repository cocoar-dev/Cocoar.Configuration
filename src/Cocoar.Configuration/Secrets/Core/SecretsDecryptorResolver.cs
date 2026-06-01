using System.Security.Cryptography;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Exceptions;

namespace Cocoar.Configuration.Secrets.Core;

internal sealed class SecretsDecryptorResolver
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretsDecryptorResolver(ConfigManagerCapabilityScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public IConfigurationAccessor ConfigAccessor => _scope.Owner.Get();

    public IRuntimeSecretDecryptor ResolveForKid(string kid)
    {
        var composition = _scope.Owner.GetComposition();
        var allDecryptors = composition?.GetAll<IRuntimeSecretDecryptor>() ?? Enumerable.Empty<IRuntimeSecretDecryptor>();

        var capableDecryptors = allDecryptors
            .Where(d => d.CanDecrypt(kid))
            .ToList();

        if (capableDecryptors.Count == 0)
        {
            // Get available kids for better error message
            var availableKids = GetAvailableKids(allDecryptors);

            // If no decryptors at all, provide setup guidance
            if (!allDecryptors.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot decrypt Secret with kid '{kid}': no certificates configured.\n\n" +
                    "To fix, configure a certificate in your secrets setup:\n\n" +
                    "  .UseSecretsSetup(secrets => secrets\n" +
                    "      .UseCertificateFromFile(\"path/to/cert.pfx\")\n" +
                    "      .WithKeyId(\"your-key-id\"))\n\n" +
                    "Or use a certificate folder:\n\n" +
                    "  .UseSecretsSetup(secrets => secrets\n" +
                    "      .UseCertificatesFromFolder(\"path/to/certs\"))");
            }

            throw SecretDecryptionException.KidNotFound(kid, "RSA-OAEP-AES256-GCM", availableKids);
        }

        return capableDecryptors.Count == 1
            ? capableDecryptors[0]
            : new MultiKeyDecryptor(kid, capableDecryptors);
    }

    /// <summary>
    /// Gets all kid values that can be decrypted by registered protectors.
    /// Used for diagnostic error messages.
    /// </summary>
    public string[] GetAvailableKids()
    {
        var composition = _scope.Owner.GetComposition();
        var allDecryptors = composition?.GetAll<IRuntimeSecretDecryptor>() ?? Enumerable.Empty<IRuntimeSecretDecryptor>();
        return GetAvailableKids(allDecryptors);
    }

    private static string[] GetAvailableKids(IEnumerable<IRuntimeSecretDecryptor> decryptors)
    {
        // Note: We can't easily enumerate all kids without trying them
        // This is a limitation of the current CanDecrypt(kid) API
        // For now, we'll return a placeholder indicating we have decryptors registered
        var count = decryptors.Count();
        return count > 0
            ? new[] { $"{count} protector(s) registered - check logs for loaded certificates" }
            : Array.Empty<string>();
    }
}

internal sealed class MultiKeyDecryptor : IRuntimeSecretEncryptor
{
    private readonly string _kid;
    private readonly List<IRuntimeSecretDecryptor> _inner;

    public MultiKeyDecryptor(string kid, IEnumerable<IRuntimeSecretDecryptor> inner)
    {
        _kid = kid;
        _inner = inner?.ToList() ?? throw new ArgumentNullException(nameof(inner));
        if (_inner.Count == 0)
            throw new ArgumentException("Multi-key decryptor requires at least one inner decryptor", nameof(inner));
    }

    public bool CanDecrypt(string kid) => string.Equals(_kid, kid, StringComparison.OrdinalIgnoreCase);

    public IEncryptedEnvelope ProtectInternal(ReadOnlySpan<byte> plaintext, string kid)
    {
        if (_inner[_inner.Count - 1] is IRuntimeSecretEncryptor encryptor)
        {
            return encryptor.ProtectInternal(plaintext, kid);
        }
        throw new NotSupportedException(
            $"The most recent decryptor for kid '{kid}' does not support encryption.");
    }

    public byte[] UnprotectInternal(IEncryptedEnvelope envelope, string kid)
    {
        Exception? lastException = null;
        for (int i = _inner.Count - 1; i >= 0; i--)
        {
            var p = _inner[i];
            try
            {
                return p.UnprotectInternal(envelope, kid);
            }
            catch (CryptographicException ex)
            {
                lastException = ex;
            }
            catch (NotSupportedException ex)
            {
                lastException = ex;
            }
        }

        // All decryptors that claimed they could handle this kid failed
        // Try to get algorithm from envelope if it's a HybridEnvelope
        var algorithm = envelope is Protectors.Hybrid.HybridEnvelope hybridEnv
            ? hybridEnv.WrappingAlgorithm
            : "RSA-OAEP-AES256-GCM";

        throw SecretDecryptionException.DecryptionFailed(
            kid,
            algorithm,
            lastException ?? new CryptographicException("All registered decryptors failed"));
    }

    public IEncryptedEnvelope DeserializeEnvelope(string json) =>
        _inner[_inner.Count - 1].DeserializeEnvelope(json);
}
