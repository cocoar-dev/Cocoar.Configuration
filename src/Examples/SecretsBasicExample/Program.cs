using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.X509Encryption;
using System.Text.Json;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace SecretsBasicExample;

/// <summary>
/// Demonstrates basic Secrets usage with self-signed certificate.
/// 
/// Key concepts:
/// - Secret<string> requires certificate-based encryption/decryption
/// - Certificate must be generated explicitly before use
/// - Values in JSON must be pre-encrypted using the CLI tool
/// - Use SecretLease with 'using' to ensure proper cleanup
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== Secrets Basic Example: Self-Signed Certificate ===\n");
        Console.WriteLine("⚠️  NOTE: This example demonstrates the API.");
        Console.WriteLine("    In production, use the CLI tool to pre-encrypt secrets.\n");

        // Generate self-signed certificate explicitly for this demo
        var certPath = Path.Combine(Path.GetTempPath(), "cocoar-secrets-demo.pfx");
        
        // Explicit certificate generation (password-less)
        X509CertificateGenerator.GenerateAndSave(
            certPath,
            null,  // Password-less certificate
            "CN=Dev Secrets",
            validYears: 1,
            keySize: 2048,
            overwrite: true);

        // Create ConfigManager with certificate-based secrets
        var manager = ConfigManager.Create(c => c
            .WithConfiguration(rule => [
                rule.For<AppConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("appsettings.json"))
            ])
            .WithSecretsSetup(secrets => secrets
                .UseCertificateFromFile(certPath)
                .WithKeyId("dev-secrets")));

        // Retrieve configuration
        var config = manager.GetConfig<AppConfig>();

        Console.WriteLine("✅ Configuration loaded with certificate-based secrets\n");
        Console.WriteLine("📋 Configuration structure:");
        Console.WriteLine($"   Database.ConnectionString: {config?.Database.ConnectionString}");  // Shows "***"
        Console.WriteLine($"   Database.ApiKey: {config?.Database.ApiKey}");                      // Shows "***"
        Console.WriteLine($"   ExternalService.ApiKey: {config?.ExternalService.ApiKey}");        // Shows "***"
        Console.WriteLine();

        // Demonstrate secure access to secrets
        Console.WriteLine("🔐 Accessing secrets securely:\n");
        Console.WriteLine("⚠️  NOTE: These are plain text values from JSON (not encrypted yet)\n");

        // CORRECT: Use 'using' to ensure memory is zeroized
        using (var dbPasswordLease = config?.Database.ConnectionString.Open())
        {
            var value = dbPasswordLease?.Value;
            if (value != null)
            {
                Console.WriteLine($"   Database connection string (first 30 chars): {value.Substring(0, Math.Min(30, value.Length))}...");
            }
        }
        // ✅ Memory automatically zeroized after 'using' block

        using (var apiKeyLease = config?.Database.ApiKey.Open())
        {
            var value = apiKeyLease?.Value;
            if (value != null)
            {
                Console.WriteLine($"   Database API key (first 15 chars): {value.Substring(0, Math.Min(15, value.Length))}...");
            }
        }

        using (var externalApiKeyLease = config?.ExternalService.ApiKey.Open())
        {
            var value = externalApiKeyLease?.Value;
            if (value != null)
            {
                Console.WriteLine($"   External service API key (first 20 chars): {value.Substring(0, Math.Min(20, value.Length))}...");
            }
        }

        Console.WriteLine();
        Console.WriteLine("✨ Key points:");
        Console.WriteLine("   • Certificate-based encryption/decryption");
        Console.WriteLine("   • Certificate explicitly generated before use");
        Console.WriteLine("   • Certificate stored at: " + certPath);
        Console.WriteLine("   • Certificate subject: CN=Dev Secrets");
        Console.WriteLine();
        Console.WriteLine("⚠️  Production checklist:");
        Console.WriteLine("   • Generate certificates using: cocoar-secrets generate-cert");
        Console.WriteLine("   • Encrypt secrets using: cocoar-secrets encrypt");
        Console.WriteLine("   • Use proper PKI certificates (not self-signed)");
        Console.WriteLine("   • Pre-encrypted secret envelopes in JSON");

        // Cleanup demo cert
        if (File.Exists(certPath))
        {
            File.Delete(certPath);
        }
        Console.WriteLine("   • Secrets shown as '***' when converted to string");
        Console.WriteLine("   • Memory zeroized after each 'using' block");
        Console.WriteLine();
        Console.WriteLine("🎯 Use case: Development & testing with explicit certificate generation");
    }
}

public class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();
    public ExternalServiceConfig ExternalService { get; set; } = new();
}

public class DatabaseConfig
{
    // Secret<string> automatically encrypts plain-text values from JSON
    public Secret<string> ConnectionString { get; set; } = null!;
    public Secret<string> ApiKey { get; set; } = null!;
}

public class ExternalServiceConfig
{
    public string Url { get; set; } = "";
    public Secret<string> ApiKey { get; set; } = null!;
}

