namespace Cocoar.Configuration.Providers.FileSourceProvider;

public record FileSourceProviderQueryOptions(
	string Filename,
	string? MemberPath = null,
	string? MemberWrapper = null,
	TimeSpan? Debounce = null
) : ISourceProviderQueryOptions;
