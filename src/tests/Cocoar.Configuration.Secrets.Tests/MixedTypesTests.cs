using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record MixedConfig
{
    public Secret<string>? Username { get; init; }
    public Secret<string>? Password { get; init; }
    public string? PublicEndpoint { get; init; }
    public Secret<int>? Port { get; init; }
    public bool Enabled { get; init; }
}

public record AllPrimitivesSecret
{
    public Secret<int>? IntValue { get; init; }
    public Secret<long>? LongValue { get; init; }
    public Secret<double>? DoubleValue { get; init; }
    public Secret<decimal>? DecimalValue { get; init; }
    public Secret<bool>? BoolValue { get; init; }
    public Secret<string>? StringValue { get; init; }
    public Secret<Guid>? GuidValue { get; init; }
}

public record ComplexWithSecrets
{
    public string? PublicName { get; init; }
    public Secret<Credentials>? Credentials { get; init; }
    public Secret<Address>? BillingAddress { get; init; }
    public int Version { get; init; }
}

public record NestedSecrets
{
    public Secret<Inner>? SecretInner { get; init; }
    public Inner? PlainInner { get; init; }
}

public class MixedTypesTests
{
    [Fact]
    public void MixedConfig_SecretAndPlainProperties()
    {
        var config = new MixedConfig
        {
            Username = Secret<string>.FromPlain("admin"),
            Password = Secret<string>.FromPlain("secret123"),
            PublicEndpoint = "https://api.example.com",
            Port = Secret<int>.FromPlain(8080),
            Enabled = true
        };

        using var userLease = config.Username!.Open();
        using var passLease = config.Password!.Open();
        using var portLease = config.Port!.Open();

        Assert.Equal("admin", userLease.Value);
        Assert.Equal("secret123", passLease.Value);
        Assert.Equal("https://api.example.com", config.PublicEndpoint);
        Assert.Equal(8080, portLease.Value);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void AllPrimitives_AsSecrets()
    {
        var guid = Guid.NewGuid();
        var config = new AllPrimitivesSecret
        {
            IntValue = Secret<int>.FromPlain(42),
            LongValue = Secret<long>.FromPlain(123456789L),
            DoubleValue = Secret<double>.FromPlain(3.14),
            DecimalValue = Secret<decimal>.FromPlain(99.99m),
            BoolValue = Secret<bool>.FromPlain(true),
            StringValue = Secret<string>.FromPlain("test"),
            GuidValue = Secret<Guid>.FromPlain(guid)
        };

        using var intLease = config.IntValue!.Open();
        using var longLease = config.LongValue!.Open();
        using var doubleLease = config.DoubleValue!.Open();
        using var decimalLease = config.DecimalValue!.Open();
        using var boolLease = config.BoolValue!.Open();
        using var stringLease = config.StringValue!.Open();
        using var guidLease = config.GuidValue!.Open();

        Assert.Equal(42, intLease.Value);
        Assert.Equal(123456789L, longLease.Value);
        Assert.Equal(3.14, doubleLease.Value, precision: 2);
        Assert.Equal(99.99m, decimalLease.Value);
        Assert.True(boolLease.Value);
        Assert.Equal("test", stringLease.Value);
        Assert.Equal(guid, guidLease.Value);
    }

    [Fact]
    public void ComplexTypes_AsSecrets()
    {
        var config = new ComplexWithSecrets
        {
            PublicName = "My Service",
            Credentials = Secret<Credentials>.FromPlain(new Credentials("user", "pass", "key")),
            BillingAddress = Secret<Address>.FromPlain(new Address("123 Main", "City", "12345")),
            Version = 1
        };

        Assert.Equal("My Service", config.PublicName);
        Assert.Equal(1, config.Version);

        using var credsLease = config.Credentials!.Open();
        Assert.Equal("user", credsLease.Value.Username);
        Assert.Equal("pass", credsLease.Value.Password);
        Assert.Equal("key", credsLease.Value.ApiKey);

        using var addrLease = config.BillingAddress!.Open();
        Assert.Equal("123 Main", addrLease.Value.Street);
        Assert.Equal("City", addrLease.Value.City);
        Assert.Equal("12345", addrLease.Value.Zip);
    }

    [Fact]
    public void NestedTypes_SecretAndPlain()
    {
        var config = new NestedSecrets
        {
            SecretInner = Secret<Inner>.FromPlain(new Inner(new Deep("secret value"))),
            PlainInner = new Inner(new Deep("plain value"))
        };

        using var secretLease = config.SecretInner!.Open();
        Assert.Equal("secret value", secretLease.Value.Deep.Value);
        Assert.Equal("plain value", config.PlainInner!.Deep.Value);
    }

    [Fact]
    public void MixedConfig_NullSecrets()
    {
        var config = new MixedConfig
        {
            Username = null,
            Password = Secret<string>.FromPlain("secret"),
            PublicEndpoint = "https://api.example.com",
            Port = null,
            Enabled = false
        };

        Assert.Null(config.Username);
        Assert.NotNull(config.Password);
        Assert.Null(config.Port);

        using var passLease = config.Password.Open();
        Assert.Equal("secret", passLease.Value);
    }

    [Fact]
    public void MultipleSecrets_IndependentLifecycles()
    {
        var secret1 = Secret<string>.FromPlain("value1");
        var secret2 = Secret<string>.FromPlain("value2");

        using (var lease1 = secret1.Open())
        {
            Assert.Equal("value1", lease1.Value);
        }

        using (var lease2 = secret2.Open())
        {
            Assert.Equal("value2", lease2.Value);
        }

        secret1.Dispose();
        Assert.Throws<ObjectDisposedException>(() => secret1.Open());

        using (var lease2Again = secret2.Open())
        {
            Assert.Equal("value2", lease2Again.Value);
        }
    }
}
