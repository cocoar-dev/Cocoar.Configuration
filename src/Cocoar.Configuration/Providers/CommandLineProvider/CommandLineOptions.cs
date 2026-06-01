using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public record CommandLineRuleOptions(
    string[]? Args = null,
    string[]? SwitchPrefixes = null,
    string? Prefix = null);

public record CommandLineProviderQueryOptions(
    string[]? Args,
    string[]? SwitchPrefixes = null,
    string? Prefix = null) : IProviderQuery;

public record CommandLineProviderOptions() : IProviderConfiguration
{
    public string? GenerateProviderKey() => "CommandLine:Global";
}
