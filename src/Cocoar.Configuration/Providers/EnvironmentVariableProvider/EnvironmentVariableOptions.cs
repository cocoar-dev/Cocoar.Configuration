using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public record EnvironmentVariableRuleOptions(string? EnvironmentPrefix = null);

public record EnvironmentVariableProviderQueryOptions(string? EnvironmentPrefix = null) : IProviderQuery;

public record EnvironmentVariableProviderOptions() : IProviderConfiguration
{
    public string GenerateProviderKey() => "Environment:Global";
}

