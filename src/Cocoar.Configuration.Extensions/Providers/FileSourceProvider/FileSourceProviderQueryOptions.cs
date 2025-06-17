namespace Cocoar.Configuration.Extensions.Providers.FileSourceProvider;

public record FileSourceProviderQueryOptions(string Filename, string? MemberPath = null, string? MemberWrapper = null) : ISourceProviderQueryOptions;
