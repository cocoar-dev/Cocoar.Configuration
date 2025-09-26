using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class StaticJsonProviderOptions(JsonElement value) : IProviderConfiguration
{
    public JsonElement Value { get; } = value;

    public string? GenerateProviderKey() => null;
}
