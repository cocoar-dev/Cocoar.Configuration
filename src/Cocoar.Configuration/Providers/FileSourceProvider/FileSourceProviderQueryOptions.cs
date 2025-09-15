using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public record FileSourceProviderQueryOptions(
	string Filename,
	TimeSpan? DebounceTime = null
) : IProviderQuery;
