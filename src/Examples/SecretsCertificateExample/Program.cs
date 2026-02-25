using Cocoar.Configuration;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace SecretsCertificateExample;

/// <summary>
/// Demonstrates production-ready Secrets usage with certificate-based decryption.
/// 
/// Key concepts:
/// - UseCertificateFromFile() for decrypting pre-encrypted secrets
/// - Certificates must be generated explicitly before use
/// - Pre-encrypted envelopes from CI/CD pipelines
/// - Certificate rotation with UseCertificatesFromFolder()
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== Secrets Certificate Example: Pre-Encrypted Envelopes ===\n");

        // Simulate different scenarios
        RunDevelopmentScenario();
        Console.WriteLine("\n" + new string('=', 60) + "\n");
        RunProductionScenario();
    }

    static void RunDevelopmentScenario()
    {
        Console.WriteLine("🔧 DEVELOPMENT SCENARIO");
        Console.WriteLine("   Pre-encrypted secrets with explicit certificate\n");

        // Generate a password-less self-signed certificate for development explicitly
        var devCertPath = Path.Combine(Path.GetTempPath(), "cocoar-dev-demo.pfx");
        
        X509CertificateGenerator.GenerateAndSave(
            devCertPath,
            null,  // Password-less certificate
            "CN=Dev Secrets",
            validYears: 1,
            keySize: 2048,
            overwrite: true);

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(rule => [
                rule.For<AppConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("appsettings.encrypted.json"))
            ])
            .WithSecretsSetup(secrets => secrets
                .UseCertificateFromFile(devCertPath)
                .WithKeyId("dev-secrets")));

        var config = manager.GetConfig<AppConfig>();

        Console.WriteLine("✅ Configuration loaded (Development mode)");
        Console.WriteLine($"   Database: {config?.Database.Host}:{config?.Database.Port}/{config?.Database.Name}");
        Console.WriteLine($"   Password: {config?.Database.Password}");  // Shows "***"
        Console.WriteLine($"   Beta features: {config?.Features.EnableBetaFeatures}");

        using (var passwordLease = config?.Database.Password.Open())
        {
            Console.WriteLine($"   Actual password: {passwordLease?.Value}");
        }

        Console.WriteLine("\n   💡 Certificate explicitly generated: CN=Dev Secrets");
        Console.WriteLine("   💡 Kid: 'dev-secrets'");
        Console.WriteLine("   💡 Secrets must be pre-encrypted with this certificate's public key");

        // Cleanup demo cert
        if (File.Exists(devCertPath))
        {
            File.Delete(devCertPath);
        }
    }

    static void RunProductionScenario()
    {
        Console.WriteLine("🏭 PRODUCTION SCENARIO");
        Console.WriteLine("   Pre-encrypted secrets from CI/CD pipeline\n");

        // Generate a password-less self-signed certificate for demonstration
        // In real production, you'd use: .UseCertificateFromFile("certs/prod.pfx")
        var prodCertPath = Path.Combine(Path.GetTempPath(), "cocoar-prod-demo.pfx");
        
        X509CertificateGenerator.GenerateAndSave(
            prodCertPath,
            null,  // Password-less certificate
            "CN=Production Secrets",
            validYears: 1,
            keySize: 2048,
            overwrite: true);

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(rule => [
                rule.For<AppConfig>().FromFile(_ => FileSourceRuleOptions.FromFilePath("appsettings.encrypted.json"))
            ])
            .WithSecretsSetup(secrets => secrets
                .UseCertificateFromFile(prodCertPath)
                .WithKeyId("prod-secrets")));

        var config = manager.GetConfig<AppConfig>();

        Console.WriteLine("✅ Configuration loaded (Production mode)");
        Console.WriteLine($"   Database: {config?.Database.Host}:{config?.Database.Port}/{config?.Database.Name}");
        Console.WriteLine($"   Password: {config?.Database.Password}");  // Shows "***"
        Console.WriteLine($"   API Endpoint: {config?.ExternalApi.Endpoint}");
        Console.WriteLine($"   API Key: {config?.ExternalApi.ApiKey}");  // Shows "***"
        Console.WriteLine($"   Beta features: {config?.Features.EnableBetaFeatures}");

        Console.WriteLine("\n   🔐 Decryption certificate:");
        Console.WriteLine("   • Certificate: CN=Production Secrets");
        Console.WriteLine("   • Kid: 'prod-secrets'");
        Console.WriteLine("   • Decrypts pre-encrypted secrets from CI/CD");

        Console.WriteLine("\n   💡 In real production:");
        Console.WriteLine("      • Generate cert: cocoar-secrets generate-cert -o certs/prod.pfx -p $PASSWORD");
        Console.WriteLine("      • Encrypt secrets: cocoar-secrets encrypt -f appsettings.json -c certs/prod.pfx");
        Console.WriteLine("      • Use .UseCertificatesFromFolder() for rotation support");
        Console.WriteLine("      • Store cert password in environment variable");
        Console.WriteLine("      • CI/CD pre-encrypts secrets before deployment");
        Console.WriteLine("      • Application only decrypts, never encrypts");

        // Cleanup demo cert
        if (File.Exists(prodCertPath))
        {
            File.Delete(prodCertPath);
        }
    }
}

public class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();
    public ExternalServiceConfig ExternalApi { get; set; } = new();
    public FeaturesConfig Features { get; set; } = new();
}

public class DatabaseConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";
    public Secret<string> Password { get; set; } = null!;
}

public class ExternalServiceConfig
{
    public string Url { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public Secret<string> ApiKey { get; set; } = null!;
}

public class FeaturesConfig
{
    public bool EnableBetaFeatures { get; set; }
}

