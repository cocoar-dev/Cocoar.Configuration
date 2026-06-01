using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record SourceConfig
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public int Port { get; init; }
}

public record TargetConfigA
{
    public Secret<string>? Username { get; init; }
    public Secret<string>? Password { get; init; }
}

public record TargetConfigB
{
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public record TargetConfigC
{
    public Secret<SourceConfig>? CompleteConfig { get; init; }
}

public class MultiProviderTests
{
    [Fact]
    public void MultipleSecretTypes_FromSameSource()
    {
        var sourceConfig = new SourceConfig
        {
            Username = "admin",
            Password = "secret123",
            Port = 8080
        };

        var configA = new TargetConfigA
        {
            Username = Secret<string>.FromPlain(sourceConfig.Username!),
            Password = Secret<string>.FromPlain(sourceConfig.Password!)
        };

        var configB = new TargetConfigB
        {
            Username = sourceConfig.Username,
            Password = sourceConfig.Password
        };

        var configC = new TargetConfigC
        {
            CompleteConfig = Secret<SourceConfig>.FromPlain(sourceConfig)
        };

        using var userLeaseA = configA.Username!.Open();
        using var passLeaseA = configA.Password!.Open();
        using var completeLeaseC = configC.CompleteConfig!.Open();

        Assert.Equal("admin", userLeaseA.Value);
        Assert.Equal("secret123", passLeaseA.Value);
        Assert.Equal("admin", configB.Username);
        Assert.Equal("secret123", configB.Password);
        Assert.Equal("admin", completeLeaseC.Value.Username);
        Assert.Equal("secret123", completeLeaseC.Value.Password);
        Assert.Equal(8080, completeLeaseC.Value.Port);
    }

    [Fact]
    public void SameValue_AsSecretString_AndSecretComplexType()
    {
        var creds = new Credentials("user", "pass", "key");

        var secretString = Secret<string>.FromPlain("user");
        var secretCreds = Secret<Credentials>.FromPlain(creds);

        using var stringLease = secretString.Open();
        using var credsLease = secretCreds.Open();

        Assert.Equal("user", stringLease.Value);
        Assert.Equal("user", credsLease.Value.Username);
    }

    [Fact]
    public void MultipleSecrets_SameType_IndependentValues()
    {
        var secret1 = Secret<string>.FromPlain("value1");
        var secret2 = Secret<string>.FromPlain("value2");
        var secret3 = Secret<string>.FromPlain("value3");

        using var lease1 = secret1.Open();
        using var lease2 = secret2.Open();
        using var lease3 = secret3.Open();

        Assert.Equal("value1", lease1.Value);
        Assert.Equal("value2", lease2.Value);
        Assert.Equal("value3", lease3.Value);
    }

    [Fact]
    public void MultipleSecrets_DifferentTypes_IndependentValues()
    {
        var secretString = Secret<string>.FromPlain("text");
        var secretInt = Secret<int>.FromPlain(42);
        var secretBool = Secret<bool>.FromPlain(true);
        var secretCreds = Secret<Credentials>.FromPlain(new Credentials("u", "p", "k"));

        using var stringLease = secretString.Open();
        using var intLease = secretInt.Open();
        using var boolLease = secretBool.Open();
        using var credsLease = secretCreds.Open();

        Assert.Equal("text", stringLease.Value);
        Assert.Equal(42, intLease.Value);
        Assert.True(boolLease.Value);
        Assert.Equal("u", credsLease.Value.Username);
    }

    [Fact]
    public void SecretCollection_MultipleItems()
    {
        var secrets = new List<Secret<string>>
        {
            Secret<string>.FromPlain("secret1"),
            Secret<string>.FromPlain("secret2"),
            Secret<string>.FromPlain("secret3")
        };

        var values = new List<string>();
        foreach (var secret in secrets)
        {
            using var lease = secret.Open();
            values.Add(lease.Value);
        }

        Assert.Equal(new[] { "secret1", "secret2", "secret3" }, values);
    }

    [Fact]
    public void NestedSecrets_BothPlainAndSecret()
    {
        var plainInner = new Inner(new Deep("plain"));
        var secretInner = Secret<Inner>.FromPlain(new Inner(new Deep("secret")));

        using var lease = secretInner.Open();

        Assert.Equal("plain", plainInner.Deep.Value);
        Assert.Equal("secret", lease.Value.Deep.Value);
    }
}
