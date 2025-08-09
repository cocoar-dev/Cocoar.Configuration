using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

namespace Cocoar.Configuration.Fluent.ProviderOptions;

// Combined options for Environment provider (instance + query) for fluent API
public sealed class EnvironmentVariableRuleOptions
{

    // Query-level
    public string? MemberPath { get; }
    public string? MemberWrapper { get; }

    public EnvironmentVariableRuleOptions(string? memberPath = null, string? memberWrapper = null)
    {
        MemberPath = memberPath;
        MemberWrapper = memberWrapper;
    }

    public EnvironmentVariableProviderOptions ToProviderOptions() => new(MemberPath);
    // Provider currently uses query.MemberPath as the prefix filter; map Prefix here for correct behavior
    public EnvironmentVariableProviderQueryOptions ToQueryOptions() => new(MemberPath, MemberWrapper);
}
