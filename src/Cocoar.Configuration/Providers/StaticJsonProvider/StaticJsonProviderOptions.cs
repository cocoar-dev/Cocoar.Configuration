using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

public sealed class StaticJsonProviderOptions(JsonElement value) : IProviderConfiguration
{
    public JsonElement Value { get; } = value;

    // Return null to indicate this provider should never be reused
    // Each static provider instance should have its own unique data
    public string? GenerateProviderKey() => null;
}
