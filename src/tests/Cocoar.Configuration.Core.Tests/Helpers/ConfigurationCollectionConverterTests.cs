using System.Text.Json;
using Cocoar.Configuration.Utilities;
using Xunit;

namespace Cocoar.Configuration.Core.Tests.Helpers;

public class ConfigurationCollectionConverterTests
{
    [Fact]
    public void JsonArray_RemainsSupported()
    {
        var config = Deserialize("""{"Values":["first","second"]}""");

        Assert.Equal(["first", "second"], config.Values);
    }

    [Fact]
    public void IndexedObject_UsesConfigurationOrderingAndCompactsGaps()
    {
        var config = Deserialize("""{"Values":{"10":"ten","2":"two","0":"zero"}}""");

        Assert.Equal(["zero", "two", "ten"], config.Values);
    }

    [Fact]
    public void JsonArrayString_BindsToArray()
    {
        var config = Deserialize("""{"Ports":"[80,443]"}""");

        Assert.Equal([80, 443], config.Ports);
    }

    [Fact]
    public void NumericDictionaryKey_RemainsAnObjectProperty()
    {
        var config = Deserialize("""{"StatusCodes":{"404":"Not Found"}}""");

        Assert.Equal("Not Found", config.StatusCodes["404"]);
    }

    [Fact]
    public void Base64ByteArray_KeepsSystemTextJsonSemantics()
    {
        var config = Deserialize("""{"Bytes":"AQID"}""");

        Assert.Equal([1, 2, 3], config.Bytes);
    }

    private static CollectionConfig Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ConfigurationDeserializer.Deserialize<CollectionConfig>(document.RootElement)!;
    }

    private sealed class CollectionConfig
    {
        public List<string> Values { get; set; } = [];
        public int[] Ports { get; set; } = [];
        public byte[] Bytes { get; set; } = [];
        public Dictionary<string, string> StatusCodes { get; set; } = new();
    }
}
