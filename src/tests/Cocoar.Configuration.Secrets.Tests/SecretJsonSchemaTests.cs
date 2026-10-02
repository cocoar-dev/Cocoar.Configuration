using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public class SecretJsonSchemaTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cocoar-schema-tests-" + Guid.NewGuid());

    public sealed class Credentials
    {
        public string? User { get; set; }
        public string? Password { get; set; }
    }

    public sealed class Settings
    {
        public string? Greeting { get; set; }
        public Secret<string>? ApiKey { get; set; }
        public Secret<Credentials>? Login { get; set; }
    }

    [Fact]
    public void Export_MarksSecrets_WithThePlaintextSchema()
    {
        var schema = SecretJsonSchema.Export(typeof(Settings), Options)["properties"]!;

        Assert.Null(schema["Greeting"]![SecretJsonSchema.Marker]);
        Assert.True(schema["ApiKey"]![SecretJsonSchema.Marker]!.GetValue<bool>());
        Assert.Equal("""["string","null"]""", schema["ApiKey"]!["type"]!.ToJsonString());
        Assert.True(schema["Login"]![SecretJsonSchema.Marker]!.GetValue<bool>());
        Assert.NotNull(schema["Login"]!["properties"]!["Password"]);
    }

    [Fact]
    public void GetCurrentEncryptionKey_ReturnsTheConfiguredPublicKey()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "secrets.pfx");
        X509CertificateGenerator.GenerateAndSavePfx(path, password: null, "CN=Test", validYears: 1, keySize: 2048).Dispose();

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Settings>().FromStaticJson("{}")])
            .UseSecretsSetup(s => s.UseCertificateFromFile(path).WithKeyId("instance-1")));

        var key = manager.GetCurrentEncryptionKey();

        Assert.Equal("instance-1", key?.Kid);
        Assert.False(string.IsNullOrEmpty(key?.PublicKey));
    }

    [Fact]
    public void GetCurrentEncryptionKey_WithoutSecretsSetup_IsNull()
    {
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules => [rules.For<Settings>().FromStaticJson("{}")]));

        Assert.Null(manager.GetCurrentEncryptionKey());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
