using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record ConfigWithSecretString
{
    public string? Name { get; init; }
    public Secret<string>? ApiKey { get; init; }
}

/// <summary>
/// Tests that error messages are helpful and actionable when secrets are misconfigured.
/// </summary>
public class ErrorMessageTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_WithSecretsButNoCertificates_ThrowsHelpfulError()
    {
        // Arrange - ConfigManager WITH setup.Secrets() but NO certificates configured
        var json = """
        {
            "Name": "TestApp",
            "ApiKey": {
                "type": "cocoar.secret",
                "version": 1,
                "kid": "my-key-id",
                "alg": "RSA-OAEP-AES256-GCM",
                "ct": "ZW5jcnlwdGVk",
                "iv": "bm9uY2U="
            }
        }
        """;

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(
                rules => [
                    rules.For<ConfigWithSecretString>().FromStaticJson(json).Required()
                ])
            .UseSecretsSetup(secrets => secrets) // Secrets enabled, but no certificates configured
        );

        // Act
        var config = manager.GetConfig<ConfigWithSecretString>();
        Assert.NotNull(config);
        Assert.NotNull(config!.ApiKey);

        // Assert - the error message should tell them to configure certificates
        var ex = Assert.Throws<InvalidOperationException>(() => config.ApiKey!.Open());

        Assert.Contains("no certificates configured", ex.Message);
        Assert.Contains("UseCertificateFromFile", ex.Message);
        Assert.Contains("UseCertificatesFromFolder", ex.Message);
        Assert.Contains("my-key-id", ex.Message);  // Should mention the kid they're trying to use
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_NoCertificates_ErrorContainsCodeExamples()
    {
        // Arrange
        var json = """
        {
            "Name": "TestApp",
            "ApiKey": {
                "type": "cocoar.secret",
                "version": 1,
                "kid": "test-key",
                "alg": "RSA-OAEP-AES256-GCM",
                "ct": "ZW5jcnlwdGVk",
                "iv": "bm9uY2U="
            }
        }
        """;

        var manager = ConfigManager.Create(c => c
            .UseConfiguration(
                rules => [rules.For<ConfigWithSecretString>().FromStaticJson(json).Required()])
            .UseSecretsSetup(secrets => secrets)
        );

        var config = manager.GetConfig<ConfigWithSecretString>();

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => config!.ApiKey!.Open());

        // Assert - error should have code examples showing certificate setup
        Assert.Contains(".UseCertificateFromFile(", ex.Message);
        Assert.Contains(".WithKeyId(", ex.Message);
        Assert.Contains(".UseSecretsSetup(", ex.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_FromEnvelopeWithoutResolver_ThrowsHelpfulError()
    {
        // This tests the direct Secret construction path where resolver is null
        // This can happen if Secret is constructed outside of ConfigManager deserialization

        // Arrange - create a valid envelope JSON element
        var envelopeJson = """
        {
            "type": "cocoar.secret",
            "version": 1,
            "kid": "orphan-key",
            "alg": "RSA-OAEP-AES256-GCM",
            "ct": "ZW5jcnlwdGVk",
            "iv": "bm9uY2U="
        }
        """;

        using var doc = JsonDocument.Parse(envelopeJson);
        var secret = Secret<string>.FromEnvelope(doc.RootElement);

        // Act & Assert - the error should be helpful
        var ex = Assert.Throws<InvalidOperationException>(() => secret.Open());

        Assert.Contains("secrets infrastructure not configured", ex.Message);
        Assert.Contains("UseSecretsSetup", ex.Message);
        Assert.Contains("ConfigManager", ex.Message);
        Assert.Contains("AddCocoarConfiguration", ex.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_FromEnvelopeWithoutResolver_ErrorIncludesTypeName()
    {
        // Arrange
        var envelopeJson = """
        {
            "type": "cocoar.secret",
            "version": 1,
            "kid": "test-key",
            "alg": "RSA-OAEP-AES256-GCM",
            "ct": "ZW5jcnlwdGVk",
            "iv": "bm9uY2U="
        }
        """;

        using var doc = JsonDocument.Parse(envelopeJson);
        var secret = Secret<string>.FromEnvelope(doc.RootElement);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => secret.Open());

        // Assert - error message includes the type name for easier debugging
        Assert.Contains("Secret<String>", ex.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void Secret_WithoutSecretsSetup_FailsAtDeserialization()
    {
        // This test documents the current behavior: without setup.Secrets(),
        // deserialization fails because no JSON converter is registered for Secret<T>.
        // The improved error message in Secret.cs won't be reached in this case.

        var json = """
        {
            "Name": "TestApp",
            "ApiKey": {
                "type": "cocoar.secret",
                "version": 1,
                "kid": "my-key-id",
                "alg": "RSA-OAEP-AES256-GCM",
                "ct": "ZW5jcnlwdGVk",
                "iv": "bm9uY2U="
            }
        }
        """;

        // Act & Assert - With Master Backplane, deserialization fails at startup
        var ex = Assert.Throws<ConfigurationDeserializationException>(() => ConfigManager.Create(c => c.UseConfiguration(
            rules => [
                rules.For<ConfigWithSecretString>().FromStaticJson(json).Required()
            ]
            // NOTE: No setup.Secrets() - deserialization will fail
        )));

        // The error is about JSON deserialization, not about secrets infrastructure
        Assert.Contains("Deserialization", ex.Message);
    }
}
