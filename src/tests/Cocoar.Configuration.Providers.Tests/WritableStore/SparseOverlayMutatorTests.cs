using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Providers;
using Cocoar.Json.Mutable;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.WritableStore;

public class SparseOverlayMutatorTests
{
    private static byte[] Empty => "{}"u8.ToArray();

    private static MutableJsonObject Base(string json)
        => (MutableJsonObject)MutableJsonDocument.Parse(Encoding.UTF8.GetBytes(json));

    private static JsonElement Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_CreatesNestedSparsePath_OnlyTouchedLeaf()
    {
        var result = SparseOverlayMutator.Set(Empty, "Nested.Count", JsonValue.Create(7), baseDom: null);

        var root = Parse(result);
        Assert.Equal(7, root.GetProperty("Nested").GetProperty("Count").GetInt32());
        // Only the touched leaf is present — no sibling defaults leaked in.
        Assert.Single(EnumerateNames(root));
        Assert.Single(EnumerateNames(root.GetProperty("Nested")));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_AlignsCasingToBase_CamelCaseBase()
    {
        var baseDom = Base("{\"smtp\":{\"port\":25}}");

        var result = SparseOverlayMutator.Set(Empty, "Smtp.Port", JsonValue.Create(587), baseDom);

        var root = Parse(result);
        // The override must land on the base's exact key casing, not create PascalCase siblings.
        Assert.True(root.TryGetProperty("smtp", out var smtp));
        Assert.Equal(587, smtp.GetProperty("port").GetInt32());
        Assert.False(root.TryGetProperty("Smtp", out _));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_DescendsBaseByBaseKey_NotDriftedOverlayKey()
    {
        // The overlay already holds an intermediate key that is byte-different from the base's key
        // (case-insensitively equal). The base descent must still follow the BASE's casing so the new
        // leaf aligns to the base layer's deeper key, independent of the drifted overlay key's casing.
        var seeded = Encoding.UTF8.GetBytes("{\"Smtp\":{\"host\":\"h\"}}");
        var baseDom = Base("{\"smtp\":{\"port\":25}}");

        var result = SparseOverlayMutator.Set(seeded, "Smtp.Port", JsonValue.Create(587), baseDom);

        var root = Parse(result);
        // Leaf aligned to the base's "port" casing — not the resolver default "Port".
        Assert.Equal(587, root.GetProperty("Smtp").GetProperty("port").GetInt32());
        Assert.False(root.GetProperty("Smtp").TryGetProperty("Port", out _));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_ExplicitNull_WritesJsonNull()
    {
        var result = SparseOverlayMutator.Set(Empty, "Host", valueNode: null, baseDom: null);

        var root = Parse(result);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Host").ValueKind);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_DefaultValuedLeaf_IsPersisted()
    {
        var result = SparseOverlayMutator.Set(Empty, "Port", JsonValue.Create(0), baseDom: null);

        var root = Parse(result);
        Assert.True(root.TryGetProperty("Port", out var port));
        Assert.Equal(0, port.GetInt32());
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Set_SecondLeafUnderSameParent_KeepsBoth()
    {
        var first = SparseOverlayMutator.Set(Empty, "Smtp.Port", JsonValue.Create(587), baseDom: null);
        var second = SparseOverlayMutator.Set(first, "Smtp.Host", JsonValue.Create("h"), baseDom: null);

        var root = Parse(second);
        Assert.Equal(587, root.GetProperty("Smtp").GetProperty("Port").GetInt32());
        Assert.Equal("h", root.GetProperty("Smtp").GetProperty("Host").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Remove_PrunesEmptyAncestors()
    {
        var seeded = Encoding.UTF8.GetBytes("{\"Smtp\":{\"Port\":587}}");

        var (bytes, removed) = SparseOverlayMutator.Remove(seeded, "Smtp.Port");

        Assert.True(removed);
        Assert.Equal("{}", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Remove_KeepsSiblings()
    {
        var seeded = Encoding.UTF8.GetBytes("{\"Smtp\":{\"Port\":587,\"Host\":\"x\"}}");

        var (bytes, removed) = SparseOverlayMutator.Remove(seeded, "Smtp.Port");

        Assert.True(removed);
        var root = Parse(bytes);
        Assert.False(root.GetProperty("Smtp").TryGetProperty("Port", out _));
        Assert.Equal("x", root.GetProperty("Smtp").GetProperty("Host").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Remove_AbsentKey_IsNoOp()
    {
        var seeded = Encoding.UTF8.GetBytes("{\"Smtp\":{\"Port\":587}}");

        var (bytes, removed) = SparseOverlayMutator.Remove(seeded, "Other.Key");

        Assert.False(removed);
        Assert.Same(seeded, bytes);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Remove_CaseInsensitiveMatch_Removes()
    {
        var seeded = Encoding.UTF8.GetBytes("{\"smtp\":{\"port\":587}}");

        var (bytes, removed) = SparseOverlayMutator.Remove(seeded, "Smtp.Port");

        Assert.True(removed);
        Assert.Equal("{}", Encoding.UTF8.GetString(bytes));
    }

    private static List<string> EnumerateNames(JsonElement obj)
    {
        var names = new List<string>();
        foreach (var p in obj.EnumerateObject())
        {
            names.Add(p.Name);
        }
        return names;
    }
}
