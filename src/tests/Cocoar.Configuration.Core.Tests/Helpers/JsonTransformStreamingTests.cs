using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Helper;
using Xunit;

namespace Cocoar.Configuration.Core.Tests.Helpers;

public class JsonTransformStreamingTests
{
    private static ReadOnlyMemory<byte> Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static JsonElement Parse(ReadOnlyMemory<byte> bytes) => JsonDocument.Parse(bytes).RootElement.Clone();

    [Fact]
    public void MountOnly_WrapsRootObject()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { a = 1 });
        var result = JsonTransform.SelectAndMount(input, selectPath: null, mountPath: "x:y");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("x", out var x));
        Assert.True(x.TryGetProperty("y", out var y));
        Assert.True(y.TryGetProperty("a", out var a));
        Assert.Equal(1, a.GetInt32());
    }

    [Fact]
    public void SelectOnly_ObjectProperty_YieldsPrimitive()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { user = new { name = "neo", age = 2 } });
        var result = JsonTransform.SelectAndMount(input, selectPath: "user:name", mountPath: null);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.String, root.ValueKind);
        Assert.Equal("neo", root.GetString());
    }

    [Fact]
    public void Select_ArrayIndex_YieldsNumber()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { items = new[] { new { id = 1 }, new { id = 2 } } });
        var result = JsonTransform.SelectAndMount(input, selectPath: "items:1:id", mountPath: null);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Number, root.ValueKind);
        Assert.Equal(2, root.GetInt32());
    }

    [Fact]
    public void SelectAndMount_WrapsSelectedSubtree()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { user = new { name = "neo", age = 2 } });
        var result = JsonTransform.SelectAndMount(input, selectPath: "user", mountPath: "root:payload");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("root", out var r));
        Assert.True(r.TryGetProperty("payload", out var payload));
        Assert.True(payload.TryGetProperty("name", out var name));
        Assert.Equal("neo", name.GetString());
        Assert.True(payload.TryGetProperty("age", out var age));
        Assert.Equal(2, age.GetInt32());
    }

    [Fact]
    public void MissingSelectPath_Throws()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { a = new { b = 1 } });
        Assert.Throws<KeyNotFoundException>(() => JsonTransform.SelectAndMount(input, selectPath: "a:c", mountPath: null));
    }

    [Fact]
    public void MountOnly_PrimitiveRoot_WrapsValue()
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(123);
        var result = JsonTransform.SelectAndMount(input, selectPath: null, mountPath: "v");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("v", out var v));
        Assert.Equal(123, v.GetInt32());
    }
}



