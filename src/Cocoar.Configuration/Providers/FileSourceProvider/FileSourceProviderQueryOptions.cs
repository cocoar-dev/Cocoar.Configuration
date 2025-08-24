using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.FileSourceProvider;

public record FileSourceProviderQueryOptions(
	string Filename,
	string? SectionPath = null,
	string? WrapperPath = null,
	TimeSpan? Debounce = null
) : ISourceProviderQueryOptions;
