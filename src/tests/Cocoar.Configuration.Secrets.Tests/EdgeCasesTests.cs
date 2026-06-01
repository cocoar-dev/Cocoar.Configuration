using System.Text.Json;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public record SerializableConfig
{
    public string? PublicField { get; init; }
    public Secret<string>? SecretField { get; init; }
    public int Number { get; init; }
}

public class EdgeCasesTests
{
    [Fact]
    public void Secret_Serialization_NoCustomConverter()
    {
        // Documents current behavior: Secret<T> serializes as empty object without custom converter
        var config = new SerializableConfig
        {
            PublicField = "visible",
            SecretField = Secret<string>.FromPlain("hidden"),
            Number = 42
        };

        var json = JsonSerializer.Serialize(config);

        Assert.Contains("\"PublicField\":\"visible\"", json);
        Assert.Contains("\"SecretField\":{}", json); // Serializes as empty object, not "***"
        Assert.Contains("\"Number\":42", json);
        Assert.DoesNotContain("hidden", json); // At least the secret value isn't leaked
    }

    [Fact]
    public void Secret_NullValue_Allowed()
    {
        // Documents current behavior: null values are accepted by Secret<T>
        var secret = Secret<string>.FromPlain(null!);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_EmptyString_Valid()
    {
        var secret = Secret<string>.FromPlain("");
        using var lease = secret.Open();
        Assert.Equal("", lease.Value);
    }

    [Fact]
    public void Secret_VeryLongString_Valid()
    {
        var longString = new string('x', 100000);
        var secret = Secret<string>.FromPlain(longString);
        using var lease = secret.Open();
        Assert.Equal(longString, lease.Value);
    }

    [Fact]
    public void Secret_Unicode_Valid()
    {
        var unicode = "Hello 世界 🌍 émojis";
        var secret = Secret<string>.FromPlain(unicode);
        using var lease = secret.Open();
        Assert.Equal(unicode, lease.Value);
    }

    [Fact]
    public void Secret_ByteArray_Empty()
    {
        var empty = Array.Empty<byte>();
        var secret = Secret<byte[]>.FromPlain(empty);
        using var lease = secret.Open();
        Assert.Empty(lease.Value);
    }

    [Fact]
    public void Secret_ByteArray_LargeArray()
    {
        var large = new byte[10000];
        Random.Shared.NextBytes(large);
        var secret = Secret<byte[]>.FromPlain(large);
        using var lease = secret.Open();
        Assert.Equal(large, lease.Value);
    }

    [Fact]
    public void Secret_Int_MinValue()
    {
        var secret = Secret<int>.FromPlain(int.MinValue);
        using var lease = secret.Open();
        Assert.Equal(int.MinValue, lease.Value);
    }

    [Fact]
    public void Secret_Int_MaxValue()
    {
        var secret = Secret<int>.FromPlain(int.MaxValue);
        using var lease = secret.Open();
        Assert.Equal(int.MaxValue, lease.Value);
    }

    [Fact]
    public void Secret_Decimal_VeryPrecise()
    {
        var precise = 123456789.123456789m;
        var secret = Secret<decimal>.FromPlain(precise);
        using var lease = secret.Open();
        Assert.Equal(precise, lease.Value);
    }

    [Fact]
    public void Secret_DoubleDispose_Safe()
    {
        var secret = Secret<string>.FromPlain("test");
        secret.Dispose();
        secret.Dispose();
    }

    [Fact]
    public void SecretLease_Dispose_Idempotent()
    {
        var secret = Secret<string>.FromPlain("test");
        var lease = secret.Open();
        lease.Dispose();
        lease.Dispose();
    }

    [Fact]
    public void Secret_OpenAfterLeaseDispose_StillWorks()
    {
        var secret = Secret<string>.FromPlain("test");
        
        using (var lease1 = secret.Open())
        {
            Assert.Equal("test", lease1.Value);
        }

        using (var lease2 = secret.Open())
        {
            Assert.Equal("test", lease2.Value);
        }
    }

    [Fact]
    public async Task Secret_ConcurrentOpen_Safe()
    {
        var secret = Secret<int>.FromPlain(42);
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            using var lease = secret.Open();
            return lease.Value;
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.Equal(42, t.Result));
    }

    [Fact]
    public void Secret_FromPlaintextConstructor_ThrowsOnOpen()
    {
        // Simulate what happens when JSON converter creates Secret<T> from plaintext
        // (not using FromPlain which is for testing)
        var secret = new Secret<string>("plaintext-password");

        // Opening should throw because it wasn't created from an envelope
        var ex = Assert.Throws<InvalidOperationException>(() => secret.Open());
        Assert.Contains("plaintext JSON instead of an encrypted envelope", ex.Message);
        Assert.Contains("Pre-encrypted envelopes are required", ex.Message);
    }

    #region Secret<T?> - Nullable Inner Type Tests

    [Fact]
    public void Secret_NullableString_WithValue()
    {
        // Secret<string?> with an actual value
        var secret = Secret<string?>.FromPlain("hello");
        using var lease = secret.Open();
        Assert.Equal("hello", lease.Value);
    }

    [Fact]
    public void Secret_NullableString_WithNull()
    {
        // Secret<string?> with null value
        var secret = Secret<string?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_NullableInt_WithValue()
    {
        // Secret<int?> with an actual value
        var secret = Secret<int?>.FromPlain(42);
        using var lease = secret.Open();
        Assert.Equal(42, lease.Value);
    }

    [Fact]
    public void Secret_NullableInt_WithNull()
    {
        // Secret<int?> with null value - properly supports Nullable<T> value types
        var secret = Secret<int?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_NullableLong_WithValue()
    {
        var secret = Secret<long?>.FromPlain(9876543210L);
        using var lease = secret.Open();
        Assert.Equal(9876543210L, lease.Value);
    }

    [Fact]
    public void Secret_NullableLong_WithNull()
    {
        var secret = Secret<long?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_NullableDouble_WithValue()
    {
        var secret = Secret<double?>.FromPlain(3.14159);
        using var lease = secret.Open();
        Assert.Equal(3.14159, lease.Value);
    }

    [Fact]
    public void Secret_NullableDouble_WithNull()
    {
        var secret = Secret<double?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_NullableBool_WithValue()
    {
        var secret = Secret<bool?>.FromPlain(true);
        using var lease = secret.Open();
        Assert.True(lease.Value);
    }

    [Fact]
    public void Secret_NullableBool_WithNull()
    {
        var secret = Secret<bool?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    [Fact]
    public void Secret_NullableGuid_WithValue()
    {
        var guid = Guid.NewGuid();
        var secret = Secret<Guid?>.FromPlain(guid);
        using var lease = secret.Open();
        Assert.Equal(guid, lease.Value);
    }

    [Fact]
    public void Secret_NullableGuid_WithNull()
    {
        var secret = Secret<Guid?>.FromPlain(null);
        using var lease = secret.Open();
        Assert.Null(lease.Value);
    }

    #endregion
}
