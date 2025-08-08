namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProviderQueryOptions(string? memberPath = null, string? memberWrapper = null)
    : ISourceProviderQueryOptions
{
    // MemberPath is used as the prefix for environment variable filtering
    public string? MemberPath { get; } = memberPath;
    public string? MemberWrapper { get; } = memberWrapper;
}
