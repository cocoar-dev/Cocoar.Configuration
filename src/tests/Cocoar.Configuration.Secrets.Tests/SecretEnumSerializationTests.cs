using System.Text.Json;
using Cocoar.Configuration.Secrets.SecretTypes;
using Xunit;

namespace Cocoar.Configuration.Secrets.Tests;

public class SecretEnumSerializationTests
{
    public enum Tier { Free = 0, Pro = 1, Enterprise = 2 }

    public record Plan(string Name, Tier Tier);

    // ---- options-level: the decrypted-envelope deserialize path uses these exact options ----

    [Fact]
    public void Options_Enum_ReadsName()
        => Assert.Equal(Tier.Pro, JsonSerializer.Deserialize<Tier>("\"Pro\"", SecretValueSerialization.Options));

    [Fact]
    public void Options_Enum_ReadsNumber_StaysBackwardCompatible()
        => Assert.Equal(Tier.Pro, JsonSerializer.Deserialize<Tier>("1", SecretValueSerialization.Options));

    [Fact]
    public void Options_Enum_WritesName_NotOrdinal()
        => Assert.Equal("\"Pro\"", JsonSerializer.Serialize(Tier.Pro, SecretValueSerialization.Options));

    [Fact]
    public void Options_Object_IsCaseInsensitive_AndReadsEnumName()
    {
        var plan = JsonSerializer.Deserialize<Plan>(
            "{\"name\":\"acme\",\"tier\":\"Enterprise\"}", SecretValueSerialization.Options);

        Assert.NotNull(plan);
        Assert.Equal("acme", plan!.Name);
        Assert.Equal(Tier.Enterprise, plan.Tier);
    }

    // ---- end-to-end via FromPlain (exercises both the ctor serialize and the Open deserialize) ----

    [Fact]
    public void FromPlain_Enum_RoundTrips()
    {
        var secret = Secret<Tier>.FromPlain(Tier.Enterprise);
        using var lease = secret.Open();
        Assert.Equal(Tier.Enterprise, lease.Value);
    }

    [Fact]
    public void FromPlain_RecordWithEnum_RoundTrips()
    {
        var plan = new Plan("Acme", Tier.Pro);
        var secret = Secret<Plan>.FromPlain(plan);
        using var lease = secret.Open();
        Assert.Equal(plan, lease.Value);
    }

    [Fact]
    public void FromPlain_NullableEnum_RoundTrips()
    {
        var secret = Secret<Tier?>.FromPlain(Tier.Free);
        using var lease = secret.Open();
        Assert.Equal(Tier.Free, lease.Value);
    }
}
