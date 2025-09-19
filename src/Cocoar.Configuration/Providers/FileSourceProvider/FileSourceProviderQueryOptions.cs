using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public record FileSourceProviderQueryOptions(
    string Filename,
    TimeSpan? DebounceTime = null
) : IProviderQuery;
