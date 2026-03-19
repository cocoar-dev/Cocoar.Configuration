using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Cocoar.Configuration.Secrets.Converters;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.Exceptions;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Folder-based hybrid protector with dynamic kid discovery.
/// Uses CertificateInventory to manage multiple certificates with automatic discovery and rotation.
/// </summary>
internal sealed class X509HybridFolderSecretProtector : ISecretEncryptor<HybridEnvelope>, IRuntimeSecretEncryptor
{
    private readonly CertificateInventory _inventory;
    private readonly X509Certificate2? _encryptionCert;
    private readonly RSA? _encryptionRsa;
    private readonly string? _encryptionKid;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new Base64UrlByteArrayConverter() }
    };

    /// <summary>
    /// Create a folder-based protector (decrypt-only).
    /// </summary>
    public X509HybridFolderSecretProtector(CertificateInventory inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    /// <summary>
    /// Create a folder-based protector with a specific cert for encryption.
    /// Decryption will try all certs in the kid-specific folder with intelligent caching.
    /// </summary>
    public X509HybridFolderSecretProtector(CertificateInventory inventory, X509Certificate2 encryptionCert, string encryptionKid)
        : this(inventory)
    {
        if (!encryptionCert.HasPrivateKey)
            throw new ArgumentException("Encryption certificate must contain a private key", nameof(encryptionCert));

        _encryptionCert = encryptionCert;
        _encryptionRsa = encryptionCert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("RSA private key not available on encryption certificate");
        _encryptionKid = encryptionKid ?? throw new ArgumentNullException(nameof(encryptionKid));
    }

    public bool CanDecrypt(string kid)
    {
        return _inventory.HasCertificatesFor(kid);
    }

    public HybridEnvelope Protect(ReadOnlySpan<byte> plaintext, string kid)
    {
        if (_encryptionCert == null || _encryptionRsa == null)
            throw new NotSupportedException("This protector is decrypt-only. No encryption certificate was provided.");

        Span<byte> dek = stackalloc byte[32];
        RandomNumberGenerator.Fill(dek);

        try
        {
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);
            byte[] ct = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using (var aes = new AesGcm(dek, tag.Length))
            {
                aes.Encrypt(iv, plaintext, ct, tag, associatedData: null);
            }

            var wrappedKey = _encryptionRsa.Encrypt(dek.ToArray(), RSAEncryptionPadding.OaepSHA256);

            return new HybridEnvelope
            {
                WrappedKey = wrappedKey,
                WrappingAlgorithm = "RSA-OAEP-256",
                Iv = iv,
                Ciphertext = ct,
                Tag = tag
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public byte[] Unprotect(HybridEnvelope envelope, string kid)
    {
        if (_inventory.TryDecryptWithKid(envelope, kid, out var plaintext))
            return plaintext;

        // Detailed error with actionable guidance
        throw SecretDecryptionException.DecryptionFailed(
            kid,
            "RSA-OAEP-AES256-GCM",
            new CryptographicException($"No certificate in kid folder '{kid}' could decrypt this envelope"));
    }

    bool IRuntimeSecretDecryptor.CanDecrypt(string kid)
        => CanDecrypt(kid);

    IEncryptedEnvelope IRuntimeSecretEncryptor.ProtectInternal(ReadOnlySpan<byte> plaintext, string kid)
        => Protect(plaintext, kid);

    byte[] IRuntimeSecretDecryptor.UnprotectInternal(IEncryptedEnvelope envelope, string kid)
        => Unprotect((HybridEnvelope)envelope, kid);

    IEncryptedEnvelope IRuntimeSecretDecryptor.DeserializeEnvelope(string json)
        => JsonSerializer.Deserialize<HybridEnvelope>(json, SerializerOptions)!;
}
