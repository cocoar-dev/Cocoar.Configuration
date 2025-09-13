using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public record FileSourceProviderQueryOptions(
	string Filename,
	string? ConfigurationPath = null,
	string? TargetPath = null,
	TimeSpan? DebounceTime = null
) : IProviderQuery;
