using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Exceptions;
using Cocoar.Configuration.Secrets.Helpers;
using Cocoar.Configuration.Secrets.Protectors.Hybrid;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public class CertificateFolderTests : IDisposable
{
    private readonly string _tempBasePath;
    private readonly string _password = "Test123!";

    public CertificateFolderTests()
    {
        _tempBasePath = Path.Combine(Path.GetTempPath(), "cocoar-cert-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBasePath))
        {
            Directory.Delete(_tempBasePath, recursive: true);
        }
    }
    
    private X509Certificate2 GenerateTestCert(string path, string subject)
    {
        X509CertificateGenerator.GenerateAndSave(path, _password, subject, validYears: 1, keySize: 2048, overwrite: true);
        return CertificateHelper.LoadFromFile(path, _password);
    }

    [Fact]
    public void KidSpecific_CertInKidFolder_CanDecrypt()
    {
        var kidFolder = Path.Combine(_tempBasePath, "pci");
        Directory.CreateDirectory(kidFolder);
        var kidCertPath = Path.Combine(kidFolder, "kid-specific.pfx");

        var kidCert = GenerateTestCert(kidCertPath, "CN=KidSpecific");

        try
        {
            var globalInventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(globalInventory);

            var envelope = CreateTestEnvelope(kidCert);
            var plaintext = protector.Unprotect(envelope, "pci");

            Assert.NotNull(plaintext);
            Assert.Equal("test-secret", System.Text.Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            kidCert.Dispose();
        }
    }

    [Fact]
    public void KidSpecific_MultipleKids_EachDecryptsWithOwnCert()
    {
        var kid1Folder = Path.Combine(_tempBasePath, "pci");
        var kid2Folder = Path.Combine(_tempBasePath, "hipaa");
        Directory.CreateDirectory(kid1Folder);
        Directory.CreateDirectory(kid2Folder);
        
        var cert1Path = Path.Combine(kid1Folder, "cert1.pfx");
        var cert2Path = Path.Combine(kid2Folder, "cert2.pfx");

        var cert1 = GenerateTestCert(cert1Path, "CN=PCI");
        var cert2 = GenerateTestCert(cert2Path, "CN=HIPAA");

        try
        {
            var globalInventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(globalInventory);

            var envelope1 = CreateTestEnvelope(cert1);
            var plaintext1 = protector.Unprotect(envelope1, "pci");
            Assert.Equal("test-secret", System.Text.Encoding.UTF8.GetString(plaintext1));

            var envelope2 = CreateTestEnvelope(cert2);
            var plaintext2 = protector.Unprotect(envelope2, "hipaa");
            Assert.Equal("test-secret", System.Text.Encoding.UTF8.GetString(plaintext2));
        }
        finally
        {
            cert1.Dispose();
            cert2.Dispose();
        }
    }

    [Fact]
    public void KidSpecific_WrongKidFolder_ThrowsException()
    {
        var kidFolder = Path.Combine(_tempBasePath, "pci");
        Directory.CreateDirectory(kidFolder);
        var kidCertPath = Path.Combine(kidFolder, "cert.pfx");

        var kidCert = GenerateTestCert(kidCertPath, "CN=PCI");

        try
        {
            var globalInventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(globalInventory);

            var envelope = CreateTestEnvelope(kidCert);
            
            // Try to decrypt with wrong kid - should fail with detailed error
            var ex = Assert.Throws<SecretDecryptionException>(() => protector.Unprotect(envelope, "hipaa"));
            Assert.Contains("Failed to decrypt secret with kid 'hipaa'", ex.Message);
            Assert.Contains("Possible causes", ex.Message);
        }
        finally
        {
            kidCert.Dispose();
        }
    }

    [Fact]
    public void KidSpecific_NoCertInKidFolder_ThrowsException()
    {
        var kidFolder = Path.Combine(_tempBasePath, "pci");
        Directory.CreateDirectory(kidFolder);

        var otherCertPath = Path.Combine(Path.GetTempPath(), "other-" + Guid.NewGuid() + ".pfx");
        var otherCert = GenerateTestCert(otherCertPath, "CN=Other");

        try
        {
            var globalInventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(globalInventory);

            var envelope = CreateTestEnvelope(otherCert);

            var ex = Assert.Throws<SecretDecryptionException>(() => protector.Unprotect(envelope, "pci"));
            Assert.Contains("Failed to decrypt secret with kid 'pci'", ex.Message);
            Assert.Contains("Possible causes", ex.Message);
        }
        finally
        {
            otherCert.Dispose();
            if (File.Exists(otherCertPath))
                File.Delete(otherCertPath);
        }
    }

    [Fact]
    public void MissingFolder_DoesNotThrow_OnInitialize()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), "cocoar-missing-" + Guid.NewGuid());
        // Intentionally NOT creating the folder

        // Should not throw when creating inventory for a missing folder
        var inventory = new CertificateInventory(missingFolder, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
        var protector = new X509HybridFolderSecretProtector(inventory);

        // Protector creation should succeed
        Assert.NotNull(protector);
        
        inventory.Dispose();
    }

    [Fact]
    public void MissingFolder_ThrowsOnDecrypt()
    {
        var missingFolder = Path.Combine(Path.GetTempPath(), "cocoar-missing-" + Guid.NewGuid());
        // Intentionally NOT creating the folder

        // Create a valid envelope with a cert from elsewhere
        var otherCertPath = Path.Combine(Path.GetTempPath(), "other-" + Guid.NewGuid() + ".pfx");
        var otherCert = GenerateTestCert(otherCertPath, "CN=Other");

        try
        {
            var inventory = new CertificateInventory(missingFolder, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(inventory);

            var envelope = CreateTestEnvelope(otherCert);

            // Should fail during decryption, not during setup
            var ex = Assert.Throws<SecretDecryptionException>(() => protector.Unprotect(envelope, "pci"));
            Assert.Contains("Failed to decrypt secret with kid 'pci'", ex.Message);
            
            inventory.Dispose();
        }
        finally
        {
            otherCert.Dispose();
            if (File.Exists(otherCertPath))
                File.Delete(otherCertPath);
        }
    }

    [Fact]
    public async Task FolderRename_DetectsNewCertificates()
    {
        // Cocoar.FileSystem 2.2.0+ properly detects folder renames containing matching files.
        // This test validates atomic folder swaps for certificate rotation (e.g., kid1 → kid2).
        
        var kid1Folder = Path.Combine(_tempBasePath, "kid1");
        Directory.CreateDirectory(kid1Folder);
        var certPath = Path.Combine(kid1Folder, "cert.pfx");
        var cert = GenerateTestCert(certPath, "CN=Kid1");

        try
        {
            var inventory = new CertificateInventory(_tempBasePath, "*.pfx", null, null, _ => [_password], 30, includeSubdirectories: -1);
            var protector = new X509HybridFolderSecretProtector(inventory);

            var envelope = CreateTestEnvelope(cert);

            // Initial decrypt with kid1 should work
            var plaintext1 = protector.Unprotect(envelope, "kid1");
            Assert.Equal("test-secret", System.Text.Encoding.UTF8.GetString(plaintext1));

            // Atomically rename folder from kid1 to kid2 (simulating key rotation)
            var kid2Folder = Path.Combine(_tempBasePath, "kid2");
            Directory.Move(kid1Folder, kid2Folder);

            // Wait for file watcher to detect the folder rename
            // Use active polling to ensure the inventory has updated
            var deadline = DateTime.UtcNow.AddSeconds(3);
            var detected = false;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var plaintext = protector.Unprotect(envelope, "kid2");
                    if (System.Text.Encoding.UTF8.GetString(plaintext) == "test-secret")
                    {
                        detected = true;
                        break;
                    }
                }
                catch
                {
                    // Not yet detected, continue waiting
                }
                await Task.Delay(50);
            }

            Assert.True(detected, "File watcher did not detect folder rename within 3 seconds");

            // Verify kid2 works after folder rename
            var plaintext2 = protector.Unprotect(envelope, "kid2");
            Assert.Equal("test-secret", System.Text.Encoding.UTF8.GetString(plaintext2));
            
            // Verify kid1 no longer works (folder was renamed, not copied)
            var ex = Assert.Throws<SecretDecryptionException>(() => protector.Unprotect(envelope, "kid1"));
            Assert.Contains("kid1", ex.Message);
            Assert.Contains("No certificate in kid folder", ex.InnerException?.Message ?? "");
            
            inventory.Dispose();
        }
        finally
        {
            cert.Dispose();
        }
    }

    private static HybridEnvelope CreateTestEnvelope(X509Certificate2 cert)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("test-secret");
        
        Span<byte> dek = stackalloc byte[32];
        RandomNumberGenerator.Fill(dek);

        byte[] iv = new byte[12];
        RandomNumberGenerator.Fill(iv);
        byte[] ct = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using (var aes = new AesGcm(dek, tag.Length))
        {
            aes.Encrypt(iv, plaintext, ct, tag, associatedData: null);
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

