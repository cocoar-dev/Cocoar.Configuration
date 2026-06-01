using System.Text.Json;

namespace Cocoar.Configuration.Core.Tests.Helpers;

/// <summary>
/// Test helper to convert byte-based provider responses back to JsonElement for test assertions.
/// This is purely for testing - in production, the ConfigManager handles the conversion internally.
/// </summary>
internal static class ByteTestHelpers
{
    /// <summary>
    /// Converts byte[] (UTF-8 JSON) to JsonElement for test assertions.
    /// </summary>
    public static JsonElement ToJsonElement(this byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
    
    /// <summary>
    /// Converts ReadOnlyMemory&lt;byte&gt; (UTF-8 JSON) to JsonElement for test assertions.
    /// </summary>
    public static JsonElement ToJsonElement(this ReadOnlyMemory<byte> bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Converts Task&lt;byte[]&gt; to Task&lt;JsonElement&gt; for test assertions.
    /// </summary>
    public static async Task<JsonElement> ToJsonElementAsync(this Task<byte[]> bytesTask)
    {
        var bytes = await bytesTask;
        return bytes.ToJsonElement();
    }
}



