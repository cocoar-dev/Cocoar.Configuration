using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Helpers;

namespace Cocoar.Configuration.Secrets.Tests;

public class CertificateExpirationTests : IDisposable
{
    private readonly string _tempPath;
    private readonly string _password = "Test123!";

    public CertificateExpirationTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "cocoar-expiration-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }

    [Fact]
    public void LoadCertificate_ValidCert_LoadsSuccessfully()
    {
        // Generate certificate valid for 1 year (minimum)
        var certPath = Path.Combine(_tempPath, "valid.pfx");
        X509CertificateGenerator.GenerateAndSave(certPath, _password, "CN=Valid", validYears: 1, keySize: 2048, overwrite: true);
        
        // Redirect console output to capture any messages
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        
        try
        {
            // Load certificate - should work without issues
            using var cert = CertificateHelper.LoadFromFile(certPath, _password);
            
            // Verify certificate loaded successfully
            Assert.NotNull(cert);
            Assert.True(cert.HasPrivateKey);
            Assert.Equal("CN=Valid", cert.Subject);
            
            // Certificate validation runs but 1-year cert won't trigger warning
            var output = sw.ToString();
            // If cert expires within 30 days, there would be a warning
            // For 1-year cert, no warning expected
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void LoadCertificate_NotExpiring_NoWarning()
    {
        // Generate certificate valid for 1 year
        var certPath = Path.Combine(_tempPath, "valid.pfx");
        X509CertificateGenerator.GenerateAndSave(certPath, _password, "CN=Valid", validYears: 1, keySize: 2048, overwrite: true);
        
        // Redirect console output to verify no warning
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        
        try
        {
            // Load certificate - should NOT trigger warning
            using var cert = CertificateHelper.LoadFromFile(certPath, _password);
            
            // Verify certificate loaded successfully
            Assert.NotNull(cert);
            Assert.True(cert.HasPrivateKey);
            
            // Verify no warning was written
            var output = sw.ToString();
            Assert.DoesNotContain("WARNING", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
