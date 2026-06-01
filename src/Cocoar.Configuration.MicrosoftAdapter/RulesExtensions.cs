using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

public static class RulesExtensions
{
    /// <summary>
    /// Creates a configuration rule that reads from an existing <see cref="IConfiguration"/> instance.
    /// Use <c>.Select("SectionName")</c> to scope to a specific section.
    /// </summary>
    /// <param name="builder">The typed rule builder.</param>
    /// <param name="configuration">
    /// The <see cref="IConfiguration"/> to read from. Accepts <see cref="IConfigurationRoot"/>,
    /// <see cref="IConfigurationSection"/>, or any <see cref="IConfiguration"/> implementation.
    /// </param>
    public static
        ProviderRuleBuilder<MicrosoftConfigurationProvider, MicrosoftConfigurationProviderOptions,
            MicrosoftConfigurationProviderQueryOptions> FromIConfiguration<T>(
            this TypedProviderBuilder<T> builder,
            IConfiguration configuration)
        where T : class
        => new(
            _ => new MicrosoftConfigurationProviderOptions(configuration),
            _ => new MicrosoftConfigurationProviderQueryOptions(),
            typeof(T)
        );
}
