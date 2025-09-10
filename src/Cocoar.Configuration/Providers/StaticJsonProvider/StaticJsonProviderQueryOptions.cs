using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

public sealed class StaticJsonProviderQueryOptions(string? wrapperPath = null) : ISourceProviderQueryOptions
{
    public string? WrapperPath { get; } = wrapperPath;
}
