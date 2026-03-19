using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.MicrosoftAdapter;

/// <summary>
/// Query options for the <see cref="MicrosoftConfigurationProvider"/>.
/// Section filtering is handled by the standard <c>.Select()</c> method on the rule builder.
/// </summary>
public sealed class MicrosoftConfigurationProviderQueryOptions : IProviderQuery;
