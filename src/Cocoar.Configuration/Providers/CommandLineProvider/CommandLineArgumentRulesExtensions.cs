using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class CommandLineArgumentRulesExtensions
{
    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedProviderBuilder<T> builder, string prefix, string[]? switchPrefixes = null)
        where T : class
        => new(
            cm => new(),
            cm => new(null, switchPrefixes, prefix),
            typeof(T)
        );

    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedProviderBuilder<T> builder, string[] switchPrefixes)
        where T : class
        => new(
            cm => new(),
            cm => new(null, switchPrefixes, null),
            typeof(T)
        );

    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedProviderBuilder<T> builder,
            Func<IConfigurationAccessor, CommandLineRuleOptions> optionsFactory)
        where T : class
        => new(
            cm => new(),
            cm => { var opts = optionsFactory(cm); return new CommandLineProviderQueryOptions(opts.Args, opts.SwitchPrefixes, opts.Prefix); },
            typeof(T)
        );
}
