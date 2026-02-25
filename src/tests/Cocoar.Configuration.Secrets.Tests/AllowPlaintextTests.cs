using System.Text.Json;
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
/// Configuration with nullable inner types: Secret&lt;T?&gt;
/// </summary>
public record ConfigWithNullableInnerSecrets
{
    public string? Name { get; init; }
    public Secret<string?>? NullableStringSecret { get; init; }
    public Secret<int?>? NullableIntSecret { get; init; }
    public Secret<bool?>? NullableBoolSecret { get; init; }
}

/// <summary>
/// Configuration with non-nullable Secret containing nullable inner type.
/// The Secret itself is required, but the value inside can be null.
/// </summary>
public record ConfigWithRequiredNullableInnerSecrets
{
    public string? Name { get; init; }
    public required Secret<string?> RequiredStringSecret { get; init; }
    public required Secret<int?> RequiredIntSecret { get; init; }
}

/// <summary>
/// Configuration with non-nullable Secret containing non-nullable inner type.
/// Neither the Secret nor the value inside can be null.
/// </summary>
public record ConfigWithNonNullableSecrets
{
    public string? Name { get; init; }
    public required Secret<string> RequiredStringSecret { get; init; }
    public required Secret<int> RequiredIntSecret { get; init; }
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

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets) // No AllowPlaintext - default security
        );

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

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

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

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext(false))
        );

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

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

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
        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext(true))
        );

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

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets
                .AllowPlaintext(false)  // First: disable
                .AllowPlaintext(true))  // Second: enable (wins)
        );

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
        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithSecret>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

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

    #region Secret<T> - Non-Nullable Inner Type Behavior with null JSON

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void JsonDeserialize_Int_FromNull_Throws()
    {
        // Verify baseline behavior: JsonSerializer.Deserialize<int>("null") should throw
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>("null"));
        Assert.Contains("could not be converted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NonNullableInnerSecret_ValueType_WithNullJson_DeserializationFails()
    {
        // Secret<int> (non-nullable value type) with null JSON:
        // The converter throws JsonException because int cannot be null.
        // With Master Backplane architecture, deserialization failures at startup throw.
        var json = """{"Name":"TestApp","RequiredStringSecret":"test","RequiredIntSecret":null}""";

        // Deserialization fails at startup with Master Backplane architecture
        var ex = Assert.Throws<ConfigurationDeserializationException>(() => ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNonNullableSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        ));
        Assert.Contains("Secret<Int32>", ex.Failures[0].Message);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NonNullableInnerSecret_ReferenceType_WithNullJson_SecretContainsNull()
    {
        // LIMITATION: Secret<string> (non-nullable reference type) with null JSON:
        // At runtime, we cannot distinguish between 'string' and 'string?' - they're the same type.
        // C# nullable reference types are compile-time only annotations.
        // Therefore, the converter creates a Secret containing null.
        var json = """{"Name":"TestApp","RequiredStringSecret":null,"RequiredIntSecret":42}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNonNullableSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        var config = manager.GetConfig<ConfigWithNonNullableSecrets>();
        Assert.NotNull(config);
        Assert.NotNull(config!.RequiredStringSecret);

        // The Secret exists but contains null - we can't enforce non-nullable reference types at runtime
        using var lease = config.RequiredStringSecret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NonNullableInnerSecret_WithValidValues_Works()
    {
        // Arrange - Secret<int> and Secret<string> with valid values
        var json = """{"Name":"TestApp","RequiredStringSecret":"password","RequiredIntSecret":42}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNonNullableSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNonNullableSecrets>();

        // Assert
        Assert.NotNull(config);
        using var stringLease = config!.RequiredStringSecret.Open();
        Assert.Equal("password", stringLease.Value);
        using var intLease = config.RequiredIntSecret.Open();
        Assert.Equal(42, intLease.Value);
    }

    #endregion

    #region Secret<T?> - Nullable Inner Type JSON Deserialization Tests

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void RequiredNullableInnerSecret_Int_WithNullValue_SecretContainsNull()
    {
        // Arrange - Secret<int?> (non-nullable container) should contain null, not BE null
        // This is the key test: when someone declares `required Secret<int?> Port`, they want
        // a Secret that contains null inside, NOT a null Secret.
        var json = """{"Name":"TestApp","RequiredStringSecret":"test","RequiredIntSecret":null}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithRequiredNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithRequiredNullableInnerSecrets>();

        // Assert - The Secret should exist and contain null
        Assert.NotNull(config);
        Assert.NotNull(config!.RequiredIntSecret);  // Secret itself is NOT null
        using var lease = config.RequiredIntSecret.Open();
        Assert.Null(lease.Value);  // But the value inside IS null
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void RequiredNullableInnerSecret_String_WithNullValue_SecretContainsNull()
    {
        // Arrange - Secret<string?> with null value
        var json = """{"Name":"TestApp","RequiredStringSecret":null,"RequiredIntSecret":42}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithRequiredNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithRequiredNullableInnerSecrets>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.RequiredStringSecret);
        using var lease = config.RequiredStringSecret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_String_WithValue_DeserializesSuccessfully()
    {
        // Arrange - Secret<string?> with an actual value
        var json = """{"Name":"TestApp","NullableStringSecret":"secret-value"}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.NullableStringSecret);
        using var lease = config.NullableStringSecret!.Open();
        Assert.Equal("secret-value", lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_String_WithNull_CreatesSecretContainingNull()
    {
        // Arrange - Secret<string?>? with explicit null JSON value
        // When the inner type accepts null (string?), the converter creates a Secret containing null
        // rather than returning null for the Secret itself. This ensures consistent behavior
        // regardless of whether the property is declared as Secret<T?>? or Secret<T?>.
        var json = """{"Name":"TestApp","NullableStringSecret":null}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert - The Secret exists but contains null
        Assert.NotNull(config);
        Assert.NotNull(config!.NullableStringSecret);
        using var lease = config.NullableStringSecret!.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_Int_WithValue_DeserializesSuccessfully()
    {
        // Arrange - Secret<int?> with an actual value
        var json = """{"Name":"TestApp","NullableIntSecret":42}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.NullableIntSecret);
        using var lease = config.NullableIntSecret!.Open();
        Assert.Equal(42, lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_Int_WithNull_CreatesSecretContainingNull()
    {
        // Arrange - Secret<int?>? with explicit null JSON value
        var json = """{"Name":"TestApp","NullableIntSecret":null}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert - The Secret exists but contains null
        Assert.NotNull(config);
        Assert.NotNull(config!.NullableIntSecret);
        using var lease = config.NullableIntSecret!.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_Bool_WithValue_DeserializesSuccessfully()
    {
        // Arrange - Secret<bool?> with an actual value
        var json = """{"Name":"TestApp","NullableBoolSecret":true}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config!.NullableBoolSecret);
        using var lease = config.NullableBoolSecret!.Open();
        Assert.True(lease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_AllTypesWithValues_DeserializesSuccessfully()
    {
        // Arrange - All nullable inner secrets with values
        var json = """{"Name":"TestApp","NullableStringSecret":"password","NullableIntSecret":123,"NullableBoolSecret":false}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("TestApp", config!.Name);

        Assert.NotNull(config.NullableStringSecret);
        using var stringLease = config.NullableStringSecret!.Open();
        Assert.Equal("password", stringLease.Value);

        Assert.NotNull(config.NullableIntSecret);
        using var intLease = config.NullableIntSecret!.Open();
        Assert.Equal(123, intLease.Value);

        Assert.NotNull(config.NullableBoolSecret);
        using var boolLease = config.NullableBoolSecret!.Open();
        Assert.False(boolLease.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "Secrets")]
    public void NullableInnerSecret_MissingFromJson_RemainsNull()
    {
        // Arrange - Secret<T?> properties not present in JSON
        var json = """{"Name":"TestApp"}""";

        var manager = ConfigManager.Create(c => c
            .WithConfiguration(
                rules => [
                    rules.For<ConfigWithNullableInnerSecrets>().FromStaticJson(json).Required()
                ])
            .WithSecretsSetup(secrets => secrets.AllowPlaintext())
        );

        // Act
        var config = manager.GetConfig<ConfigWithNullableInnerSecrets>();

        // Assert - All Secret<T?> properties should be null (not set)
        Assert.NotNull(config);
        Assert.Equal("TestApp", config!.Name);
        Assert.Null(config.NullableStringSecret);
        Assert.Null(config.NullableIntSecret);
        Assert.Null(config.NullableBoolSecret);
    }

    #endregion
}
