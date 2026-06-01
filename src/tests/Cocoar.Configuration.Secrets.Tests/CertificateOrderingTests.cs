using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Helpers;
using Cocoar.Configuration.Secrets.Protectors.Hybrid;

namespace Cocoar.Configuration.Secrets.Tests;

public class CertificateOrderingTests : IDisposable
{
    private readonly string _tempBasePath;
    private readonly string _password = "Test123!";

    public CertificateOrderingTests()
    {
        _tempBasePath = Path.Combine(Path.GetTempPath(), "cocoar-cert-order-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBasePath))
        {
            Directory.Delete(_tempBasePath, recursive: true);
        }
    }

    [Fact]
    public void DefaultOrdering_DescendingAlphabetical()
    {
        var cert01Path = Path.Combine(_tempBasePath, "01-old.pfx");
        var cert02Path = Path.Combine(_tempBasePath, "02-middle.pfx");
        var cert03Path = Path.Combine(_tempBasePath, "03-new.pfx");

        var cert01 = X509CertificateGenerator.GenerateAndSavePfx(cert01Path, _password, "CN=Cert01");
        var cert02 = X509CertificateGenerator.GenerateAndSavePfx(cert02Path, _password, "CN=Cert02");
        var cert03 = X509CertificateGenerator.GenerateAndSavePfx(cert03Path, _password, "CN=Cert03");

        try
        {
            var inventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30);

            var envelope = CreateTestEnvelope(cert03, "newest-cert");
            var plaintext = inventory.TryDecrypt(envelope, out var result);

            Assert.True(plaintext);
            Assert.Equal("newest-cert", System.Text.Encoding.UTF8.GetString(result));
        }
        finally
        {
            cert01.Dispose();
            cert02.Dispose();
            cert03.Dispose();
        }
    }

    [Fact]
    public void CustomComparer_LastWriteTimeDescending()
    {
        var cert01Path = Path.Combine(_tempBasePath, "cert-a.pfx");
        var cert02Path = Path.Combine(_tempBasePath, "cert-b.pfx");
        var cert03Path = Path.Combine(_tempBasePath, "cert-c.pfx");

        var cert01 = X509CertificateGenerator.GenerateAndSavePfx(cert01Path, _password, "CN=Cert01");
        Thread.Sleep(100);
        var cert02 = X509CertificateGenerator.GenerateAndSavePfx(cert02Path, _password, "CN=Cert02");
        Thread.Sleep(100);
        var cert03 = X509CertificateGenerator.GenerateAndSavePfx(cert03Path, _password, "CN=Cert03");

        try
        {
            var comparer = Comparer<FileInfo>.Create((a, b) =>
                b.LastWriteTime.CompareTo(a.LastWriteTime));

            var inventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, comparer);

            var envelope = CreateTestEnvelope(cert03, "newest-by-time");
            var plaintext = inventory.TryDecrypt(envelope, out var result);

            Assert.True(plaintext);
            Assert.Equal("newest-by-time", System.Text.Encoding.UTF8.GetString(result));
        }
        finally
        {
            cert01.Dispose();
            cert02.Dispose();
            cert03.Dispose();
        }
    }

    [Fact]
    public void CustomComparer_NumericSuffixDescending()
    {
        var cert01Path = Path.Combine(_tempBasePath, "cert.01.pfx");
        var cert02Path = Path.Combine(_tempBasePath, "cert.02.pfx");
        var cert03Path = Path.Combine(_tempBasePath, "cert.03.pfx");

        var cert01 = X509CertificateGenerator.GenerateAndSavePfx(cert01Path, _password, "CN=Cert01");
        var cert02 = X509CertificateGenerator.GenerateAndSavePfx(cert02Path, _password, "CN=Cert02");
        var cert03 = X509CertificateGenerator.GenerateAndSavePfx(cert03Path, _password, "CN=Cert03");

        try
        {
            var comparer = Comparer<FileInfo>.Create((a, b) =>
            {
                var aNum = ExtractNumber(a.Name);
                var bNum = ExtractNumber(b.Name);
                return bNum.CompareTo(aNum);
            });

            var inventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, comparer);

            var envelope = CreateTestEnvelope(cert03, "suffix-03");
            var plaintext = inventory.TryDecrypt(envelope, out var result);

            Assert.True(plaintext);
            Assert.Equal("suffix-03", System.Text.Encoding.UTF8.GetString(result));
        }
        finally
        {
            cert01.Dispose();
            cert02.Dispose();
            cert03.Dispose();
        }

        static int ExtractNumber(string filename)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
            var parts = nameWithoutExt.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[^1], out var num))
                return num;
            return 0;
        }
    }

    [Fact]
    public void CustomComparer_ByFileSize_LargestFirst()
    {
        var smallCertPath = Path.Combine(_tempBasePath, "small.pfx");
        var mediumCertPath = Path.Combine(_tempBasePath, "medium.pfx");
        var largeCertPath = Path.Combine(_tempBasePath, "large.pfx");

        var smallCert = X509CertificateGenerator.GenerateAndSavePfx(smallCertPath, _password, "CN=Small", keySize: 2048);
        var mediumCert = X509CertificateGenerator.GenerateAndSavePfx(mediumCertPath, _password, "CN=Medium", keySize: 3072);
        var largeCert = X509CertificateGenerator.GenerateAndSavePfx(largeCertPath, _password, "CN=Large", keySize: 4096);

        try
        {
            var comparer = Comparer<FileInfo>.Create((a, b) =>
                b.Length.CompareTo(a.Length));

            var inventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, comparer);

            var envelope = CreateTestEnvelope(largeCert, "largest-cert");
            var plaintext = inventory.TryDecrypt(envelope, out var result);

            Assert.True(plaintext);
            Assert.Equal("largest-cert", System.Text.Encoding.UTF8.GetString(result));
        }
        finally
        {
            smallCert.Dispose();
            mediumCert.Dispose();
            largeCert.Dispose();
        }
    }

    private static HybridEnvelope CreateTestEnvelope(X509Certificate2 cert, string? plaintext = null)
    {
        var certName = cert.Subject.Replace("CN=", "").ToLowerInvariant();
        var data = System.Text.Encoding.UTF8.GetBytes(plaintext ?? certName);
        
        Span<byte> dek = stackalloc byte[32];
        RandomNumberGenerator.Fill(dek);

        byte[] iv = new byte[12];
        RandomNumberGenerator.Fill(iv);
        byte[] ct = new byte[data.Length];
        byte[] tag = new byte[16];

        using (var aes = new AesGcm(dek, tag.Length))
        {
            aes.Encrypt(iv, data, ct, tag, associatedData: null);
        }

        using var rsa = cert.GetRSAPublicKey()!;
        var wrappedKey = rsa.Encrypt(dek.ToArray(), RSAEncryptionPadding.OaepSHA256);

        CryptographicOperations.ZeroMemory(dek);

        return new HybridEnvelope
        {
            WrappedKey = wrappedKey,
            WrappingAlgorithm = "RSA-OAEP-256",
            Iv = iv,
            Ciphertext = ct,
            Tag = tag
        };
    }
}
