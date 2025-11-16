using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets.Tests;

public class PrimitivesTests
{
    [Fact]
    public void Secret_Int_PlainValue()
    {
        var secret = Secret<int>.FromPlain(42);
        using var lease = secret.Open();
        Assert.Equal(42, lease.Value);
    }

    [Fact]
    public void Secret_Long_PlainValue()
    {
        var secret = Secret<long>.FromPlain(123456789L);
        using var lease = secret.Open();
        Assert.Equal(123456789L, lease.Value);
    }

    [Fact]
    public void Secret_Double_PlainValue()
    {
        var secret = Secret<double>.FromPlain(3.14159);
        using var lease = secret.Open();
        Assert.Equal(3.14159, lease.Value, precision: 5);
    }

    [Fact]
    public void Secret_Decimal_PlainValue()
    {
        var secret = Secret<decimal>.FromPlain(99.99m);
        using var lease = secret.Open();
        Assert.Equal(99.99m, lease.Value);
    }

    [Fact]
    public void Secret_Bool_True()
    {
        var secret = Secret<bool>.FromPlain(true);
        using var lease = secret.Open();
        Assert.True(lease.Value);
    }

    [Fact]
    public void Secret_Bool_False()
    {
        var secret = Secret<bool>.FromPlain(false);
        using var lease = secret.Open();
        Assert.False(lease.Value);
    }

    [Fact]
    public void Secret_String_PlainValue()
    {
        var secret = Secret<string>.FromPlain("hello world");
        using var lease = secret.Open();
        Assert.Equal("hello world", lease.Value);
    }

    [Fact]
    public void Secret_String_Empty()
    {
        var secret = Secret<string>.FromPlain("");
        using var lease = secret.Open();
        Assert.Equal("", lease.Value);
    }

    [Fact]
    public void Secret_String_Multiline()
    {
        var text = "line1\nline2\r\nline3";
        var secret = Secret<string>.FromPlain(text);
        using var lease = secret.Open();
        Assert.Equal(text, lease.Value);
    }

    [Fact]
    public void Secret_String_SpecialChars()
    {
        var text = "\"quotes\" and 'apostrophes' and {braces} and [brackets]";
        var secret = Secret<string>.FromPlain(text);
        using var lease = secret.Open();
        Assert.Equal(text, lease.Value);
    }

    [Fact]
    public void Secret_Guid_PlainValue()
    {
        var guid = Guid.NewGuid();
        var secret = Secret<Guid>.FromPlain(guid);
        using var lease = secret.Open();
        Assert.Equal(guid, lease.Value);
    }

    [Fact]
    public void Secret_DateTime_PlainValue()
    {
        var now = DateTime.UtcNow;
        var secret = Secret<DateTime>.FromPlain(now);
        using var lease = secret.Open();
        Assert.Equal(now, lease.Value);
    }

    [Fact]
    public void Secret_DateTimeOffset_PlainValue()
    {
        var now = DateTimeOffset.UtcNow;
        var secret = Secret<DateTimeOffset>.FromPlain(now);
        using var lease = secret.Open();
        Assert.Equal(now, lease.Value);
    }

    [Fact]
    public void Secret_ByteArray_Base64String()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var secret = Secret<byte[]>.FromPlain(bytes);
        using var lease = secret.Open();
        Assert.Equal(bytes, lease.Value);
    }

    [Fact]
    public void Secret_ByteArray_PlainString()
    {
        var text = "not base64!@#";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var secret = Secret<byte[]>.FromPlain(bytes);
        using var lease = secret.Open();
        Assert.Equal(bytes, lease.Value);
    }

    [Fact]
    public void Secret_ToString_ReturnsStars()
    {
        var secret = Secret<int>.FromPlain(42);
        Assert.Equal("***", secret.ToString());
    }

    [Fact]
    public void Secret_Dispose_ZeroizesBytes()
    {
        var secret = Secret<string>.FromPlain("sensitive");
        secret.Dispose();
        Assert.Throws<ObjectDisposedException>(() => secret.Open());
    }

    [Fact]
    public void Secret_MultipleOpen_SameValue()
    {
        var secret = Secret<int>.FromPlain(42);
        using var lease1 = secret.Open();
        using var lease2 = secret.Open();
        Assert.Equal(42, lease1.Value);
        Assert.Equal(42, lease2.Value);
    }
}
