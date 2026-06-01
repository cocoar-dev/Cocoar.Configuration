using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Protectors.Hybrid;

namespace Cocoar.Configuration.Core.Tests.Core;

/// <summary>
/// Tests for the UseSecretsSetup() extension method on ConfigManagerBuilder.
/// </summary>
public class SecretsFluentApiTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secrets_AcceptsFluentConfiguration()
    {
        // Arrange & Act
        var kid = "test-key";
        var pfxPath = Path.Combine(Path.GetTempPath(), $"{kid}.pfx");

        try
        {
            // Generate certificate explicitly
            X509CertificateGenerator.GenerateAndSavePfx(
                pfxPath,
                null,  // Password-less certificate
                "CN=Test Certificate",
                validYears: 1,
                keySize: 2048);

            var manager = ConfigManager.Create(c => c
                .UseConfiguration(rules: Array.Empty<ConfigRule>())
                .UseSecretsSetup(secrets => secrets
                    .UseCertificateFromFile(pfxPath)
                    .WithKeyId(kid)));

            // Assert
            Assert.NotNull(manager);
        }
        finally
        {
            if (File.Exists(pfxPath))
                File.Delete(pfxPath);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secrets_AllowsFluentProtectorConfiguration()
    {
        // Arrange
        var kid = $"test-{Guid.NewGuid():N}";

        var pfxPath = Path.Combine(Path.GetTempPath(), $"{kid}.pfx");

        try
        {
            // Generate certificate explicitly
            X509CertificateGenerator.GenerateAndSavePfx(
                pfxPath,
                null,  // Password-less certificate
                "CN=Test Certificate",
                validYears: 1,
                keySize: 2048);

            var manager = ConfigManager.Create(c => c
                .UseConfiguration(rules: Array.Empty<ConfigRule>())
                .UseSecretsSetup(secrets => secrets
                    .UseCertificateFromFile(pfxPath)
                    .WithKeyId(kid)));

            // Assert
            Assert.NotNull(manager);

            // Verify the capability-based implementation was used by checking the composition
            // Use Owner.GetComposition() - no generic parameter needed with ConfigManagerCapabilityScope!
            var composition = manager.CapabilityScope.Owner.GetComposition();
            Assert.NotNull(composition);

            // Should have SecretsSetupDeferredConfiguration
            Assert.True(composition!.Has<SecretsSetupDeferredConfiguration>());

            // Should have CertificateProtectorConfig (unified)
            Assert.True(composition.Has<CertificateProtectorConfig>());
        }
        finally
        {
            if (File.Exists(pfxPath))
                File.Delete(pfxPath);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secrets_EachConfigManagerGetsIsolatedRuntime()
    {
        // Arrange
        var kid1 = $"manager1-{Guid.NewGuid():N}";
        var kid2 = $"manager2-{Guid.NewGuid():N}";
        var pfxPath1 = Path.Combine(Path.GetTempPath(), $"{kid1}.pfx");
        var pfxPath2 = Path.Combine(Path.GetTempPath(), $"{kid2}.pfx");

        try
        {
            // Generate certificates explicitly
            X509CertificateGenerator.GenerateAndSavePfx(
                pfxPath1,
                null,  // Password-less certificate
                "CN=Test Certificate 1",
                validYears: 1,
                keySize: 2048);

            X509CertificateGenerator.GenerateAndSavePfx(
                pfxPath2,
                null,  // Password-less certificate
                "CN=Test Certificate 2",
                validYears: 1,
                keySize: 2048);

            // Act - create two different ConfigManagers with different secrets configurations
            var manager1 = ConfigManager.Create(c => c
                .UseConfiguration(rules: Array.Empty<ConfigRule>())
                .UseSecretsSetup(secrets => secrets
                    .UseCertificateFromFile(pfxPath1)
                    .WithKeyId(kid1)));

            var manager2 = ConfigManager.Create(c => c
                .UseConfiguration(rules: Array.Empty<ConfigRule>())
                .UseSecretsSetup(secrets => secrets
                    .UseCertificateFromFile(pfxPath2)
                    .WithKeyId(kid2)));

            // Assert - both should be created successfully without interfering with each other
            Assert.NotNull(manager1);
            Assert.NotNull(manager2);

            // Note: We'd need to expose the runtime or capabilities API to verify complete isolation,
            // but this test at least proves that two different managers can be created with
            // different secrets configurations without errors
        }
        finally
        {
            if (File.Exists(pfxPath1))
                File.Delete(pfxPath1);
            if (File.Exists(pfxPath2))
                File.Delete(pfxPath2);
        }
    }
}
