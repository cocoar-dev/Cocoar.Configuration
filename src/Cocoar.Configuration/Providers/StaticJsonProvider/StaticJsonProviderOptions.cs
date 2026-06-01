using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;


public record StaticJsonProviderQueryOptions() : IProviderQuery;

public record StaticJsonProviderOptions(JsonElement Value) : IProviderConfiguration
{
    public string? GenerateProviderKey() => null;
}
