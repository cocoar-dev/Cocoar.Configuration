using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Cocoar.Configuration.X509Encryption;

/// <summary>
/// Hybrid RSA+AES encryption using X.509 certificates.
/// Uses RSA-OAEP-SHA256 for key wrapping and AES-256-GCM for data encryption.
/// </summary>
public sealed class X509HybridCrypto
{
    private readonly X509Certificate2 _certificate;
    private readonly RSA _rsa;

    /// <summary>
    /// Create a crypto instance from a certificate with private key.
    /// </summary>
    public X509HybridCrypto(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
            throw new ArgumentException("Certificate must have a private key for encryption/decryption", nameof(certificate));

        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _rsa = certificate.GetRSAPrivateKey() 
            ?? throw new InvalidOperationException("RSA private key not available on certificate");
    }

    /// <summary>
    /// Encrypt plaintext bytes into a hybrid envelope.
    /// </summary>
    public HybridSecretEnvelope Encrypt(ReadOnlySpan<byte> plaintext)
    {
        // Generate random 256-bit AES key (DEK - Data Encryption Key)
        Span<byte> dek = stackalloc byte[32];
        RandomNumberGenerator.Fill(dek);

        try
        {
            // Generate random 96-bit IV for AES-GCM
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);

            // Encrypt with AES-256-GCM
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using (var aes = new AesGcm(dek, tag.Length))
            {
                aes.Encrypt(iv, plaintext, ciphertext, tag, associatedData: null);
            }

            // Wrap DEK with RSA-OAEP-SHA256
            var wrappedKey = _rsa.Encrypt(dek.ToArray(), RSAEncryptionPadding.OaepSHA256);

            return new HybridSecretEnvelope
            {
                WrappedKey = Convert.ToBase64String(wrappedKey),
                WrappingAlgorithm = "RSA-OAEP-256",
                Iv = Convert.ToBase64String(iv),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };
        }
        finally
        {
            // Zero out the DEK from memory
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>
    /// Encrypt a UTF-8 string into a hybrid envelope.
    /// </summary>
    public HybridSecretEnvelope Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Encrypt(bytes.AsSpan());
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>
    /// Decrypt a hybrid envelope back to plaintext bytes.
    /// </summary>
    public byte[] Decrypt(HybridSecretEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Unwrap the AES key with RSA
        var wrappedKey = Convert.FromBase64String(envelope.WrappedKey);
        var dek = _rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);

        try
        {
            // Decrypt with AES-256-GCM
            var iv = Convert.FromBase64String(envelope.Iv);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);

            var plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(dek, tag.Length))
            {
                aes.Decrypt(iv, ciphertext, tag, plaintext, associatedData: null);
            }

            return plaintext;
        }
        finally
        {
            // Zero out the DEK from memory
            Array.Clear(dek);
        }
    }

    /// <summary>
    /// Decrypt a hybrid envelope to a UTF-8 string.
    /// </summary>
    public string DecryptToString(HybridSecretEnvelope envelope)
    {
        var bytes = Decrypt(envelope);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>
    /// Load a certificate from a PFX file.
    /// </summary>
    public static X509Certificate2 LoadCertificate(string pfxPath, string password)
    {
        if (!File.Exists(pfxPath))
            throw new FileNotFoundException($"Certificate file not found: {pfxPath}", pfxPath);

        return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, X509KeyStorageFlags.Exportable);
    }
}
