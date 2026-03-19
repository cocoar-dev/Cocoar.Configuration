using System.Text.Json;
using Cocoar.Configuration.Secrets.Converters;

namespace Cocoar.Configuration.Secrets.Testing;

/// <summary>
/// Provides serialization options for test scenarios where secrets
/// need to survive FromStatic serialization.
/// </summary>
internal static class TestSecretSerialization
{
    private static readonly Lazy<JsonSerializerOptions> s_options = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Default);
        options.Converters.Add(new PlaintextSecretJsonConverterFactory());
        return options;
    });

    /// <summary>
    /// Gets JsonSerializerOptions that serialize Secret&lt;T&gt; as primitive values.
    /// </summary>
    public static JsonSerializerOptions Options => s_options.Value;
}
