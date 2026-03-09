using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class CommandLineArgumentRulesExtensions
{
    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedRuleBuilder<T> builder, string prefix, string[]? switchPrefixes = null)
        => new(
            cm => new(),
            cm => new(null, switchPrefixes, prefix),
            typeof(T)
        );

    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedRuleBuilder<T> builder, string[] switchPrefixes)
        => new(
            cm => new(),
            cm => new(null, switchPrefixes, null),
            typeof(T)
        );

    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedRuleBuilder<T> builder,
            Func<IConfigurationAccessor, CommandLineRuleOptions> optionsFactory)
        => new(
            cm => new(),
            cm => { var opts = optionsFactory(cm); return new CommandLineProviderQueryOptions(opts.Args, opts.SwitchPrefixes, opts.Prefix); },
            typeof(T)
        );
}
