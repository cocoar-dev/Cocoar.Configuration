using System.Text.Json;

namespace Cocoar.Configuration.Providers.Tests.Helpers;

/// <summary>
/// Test helper to convert byte-based provider responses back to JsonElement for test assertions.
/// This is purely for testing – in production, the ConfigManager handles the conversion internally.
/// </summary>
internal static class ByteTestHelpers
{
    /// <summary>
    /// Converts ReadOnlyMemory&lt;byte&gt; (UTF-8 JSON) to JsonElement for test assertions.
    /// If parsing fails (e.g., due to comments or malformed JSON), returns an empty JSON object {{}}.
    /// </summary>
    public static JsonElement ToJsonElement(this ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch
        {
            // For tests that intentionally feed invalid JSON (comments, etc.),
            // return an empty object instead of throwing to keep assertions simple.
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    /// <summary>
    /// Converts byte[] (UTF-8 JSON) to JsonElement for test assertions.
    /// If parsing fails (e.g., due to comments or malformed JSON), returns an empty JSON object {{}}.
    /// </summary>
    public static JsonElement ToJsonElement(this byte[] bytes)
    {
        return ((ReadOnlyMemory<byte>)bytes).ToJsonElement();
    }

    /// <summary>
    /// Converts Task&lt;ReadOnlyMemory&lt;byte&gt;&gt; to Task&lt;JsonElement&gt; for test assertions.
    /// </summary>
    public static async Task<JsonElement> ToJsonElementAsync(this Task<byte[]> bytesTask)
    {
        var bytes = await bytesTask;
        return bytes.ToJsonElement();
    }
}
