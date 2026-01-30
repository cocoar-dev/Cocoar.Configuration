using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record ConfigWithSecret
{
    public string? Name { get; init; }
    public Secret<string>? Password { get; init; }
    public Secret<int>? ApiKey { get; init; }
}

/// <summary>
/// Tests for the AllowPlaintext() fluent API method.
/// </summary>
public class AllowPlaintextTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void PlaintextSecret_WithoutAllowPlaintext_ThrowsOnOpen()
    {
        // Arrange - ConfigManager without AllowPlaintext (default behavior)
        var json = """{"Name":"TestApp","Password":"secret123"}""";

        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets() // No AllowPlaintext - default security
            ]
        ).Initialize();

        // Act
        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert - deserialization succeeds, but Open() throws
        Assert.NotNull(config);
        Assert.NotNull(config!.Password);
        var ex = Assert.Throws<InvalidOperationException>(() => { config.Password!.Open(); });
        Assert.Contains("plaintext JSON instead of an encrypted envelope", ex.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void PlaintextSecret_WithAllowPlaintextTrue_DeserializesSuccessfully()
    {
        // Arrange - ConfigManager with AllowPlaintext(true)
        var json = """{"Name":"TestApp","Password":"secret123","ApiKey":42}""";

        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets().AllowPlaintext()  // defaults to true
            ]
        ).Initialize();

        // Act
        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert - both deserialization and Open() succeed
        Assert.NotNull(config);
        Assert.Equal("TestApp", config!.Name);

        Assert.NotNull(config.Password);
        using var passwordLease = config.Password!.Open();
        Assert.Equal("secret123", passwordLease.Value);

        Assert.NotNull(config.ApiKey);
        using var apiKeyLease = config.ApiKey!.Open();
        Assert.Equal(42, apiKeyLease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void PlaintextSecret_WithAllowPlaintextFalse_StillBlocked()
    {
        // Arrange - Explicitly set AllowPlaintext(false) for self-documenting code
        var json = """{"Name":"TestApp","Password":"secret123"}""";

        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets().AllowPlaintext(false)  // explicit disable
            ]
        ).Initialize();

        // Act
        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert - same as default: Open() throws
        Assert.NotNull(config);
        Assert.NotNull(config!.Password);
        var ex = Assert.Throws<InvalidOperationException>(() => { config.Password!.Open(); });
        Assert.Contains("plaintext JSON instead of an encrypted envelope", ex.Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void AllowPlaintext_WorksWithNumericSecrets()
    {
        // Arrange
        var json = """{"Name":"Test","ApiKey":12345}""";

        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets().AllowPlaintext()
            ]
        ).Initialize();

        // Act
        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.ApiKey);
        using var lease = config.ApiKey!.Open();
        Assert.Equal(12345, lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void AllowPlaintext_ChainedWithOtherMethods()
    {
        // Arrange - AllowPlaintext should work in a fluent chain
        var json = """{"Name":"TestApp","Password":"secret123"}""";

        // This test verifies fluent API chaining compiles and works
        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets()
                    .AllowPlaintext(true)  // Can be chained
            ]
        ).Initialize();

        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.Password);
        using var lease = config.Password!.Open();
        Assert.Equal("secret123", lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void AllowPlaintext_LastCallWins()
    {
        // Arrange - If called multiple times, last value wins
        var json = """{"Name":"TestApp","Password":"secret123"}""";

        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets()
                    .AllowPlaintext(false)  // First: disable
                    .AllowPlaintext(true)   // Second: enable (wins)
            ]
        ).Initialize();

        var config = manager.GetConfig<ConfigWithSecret>();

        // Assert - The last call (true) should win
        Assert.NotNull(config);
        Assert.NotNull(config!.Password);
        using var lease = config.Password!.Open();
        Assert.Equal("secret123", lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void AllowPlaintext_DoesNotAffectEnvelopeSecrets()
    {
        // Arrange - A config type with a valid envelope structure
        // The envelope format requires: type="cocoar.secret", version=1, kid=<key-id>
        var json = """
        {
            "Name": "TestApp",
            "Password": {
                "type": "cocoar.secret",
                "version": 1,
                "kid": "test-key",
                "alg": "RSA-OAEP-AES256-GCM",
                "ct": "ZW5jcnlwdGVk",
                "iv": "bm9uY2U="
            }
        }
        """;

        // Without a certificate, we can't actually decrypt, but we can verify:
        // 1. The envelope is recognized as an envelope (not plaintext)
        // 2. AllowPlaintext doesn't break envelope handling
        var manager = new ConfigManager(
            rules => [
                rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
            ],
            setup => [
                setup.Secrets().AllowPlaintext()  // Should not affect envelopes
            ]
        ).Initialize();

        var config = manager.GetConfig<ConfigWithSecret>();

        // The secret should be deserialized (it's an envelope)
        Assert.NotNull(config);
        Assert.NotNull(config!.Password);

        // Open() will fail because we don't have a certificate, but the error
        // should be about decryption/resolver failure, not about plaintext being blocked
        var ex = Record.Exception(() => { config.Password!.Open(); });
        Assert.NotNull(ex);
        Assert.DoesNotContain("plaintext JSON", ex.Message);  // Not a plaintext error
        Assert.Contains("test-key", ex.Message);  // Error is about the missing certificate
    }
}
