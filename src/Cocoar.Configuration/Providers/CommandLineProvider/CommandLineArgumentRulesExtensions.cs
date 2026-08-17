using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers;

public static class CommandLineArgumentRulesExtensions
{
    /// <summary>
    /// Parses command-line arguments using the default <c>--</c> switch prefix and no key-prefix filter —
    /// every <c>--switch</c> maps into the configuration.
    /// </summary>
    /// <remarks>
    /// A value that itself begins with a switch prefix (e.g. <c>--port -5</c>) is parsed as a boolean flag,
    /// not a value; write <c>--port=-5</c> for such values.
    /// </remarks>
    public static
        ProviderRuleBuilder<CommandLineArgumentProvider, CommandLineProviderOptions,
            CommandLineProviderQueryOptions> FromCommandLine<T>(this TypedProviderBuilder<T> builder)
        where T : class
        => new(
            cm => new(),
            cm => new(null, null, null),
            typeof(T)
        );

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
