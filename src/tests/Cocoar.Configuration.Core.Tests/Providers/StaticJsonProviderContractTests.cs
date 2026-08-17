using System.Text.Json;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Providers;

[Trait("Category", "Unit")]
[Trait("Component", "StaticJsonProvider")]
public class StaticJsonProviderContractTests
{
    /// <summary>
    /// <c>ConfigurationProvider</c> requires a provider to "return fresh arrays a caller may zero", and
    /// <c>RuleManager.ComputeAsync</c> does zero every buffer it receives. Handing out the provider's own cached
    /// array therefore leaves the next fetch reading a run of zero bytes.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task FetchDoesNotHandOutTheProvidersOwnBuffer()
    {
        using var document = JsonDocument.Parse("""{"Region":"eu"}""");
        var provider = new StaticJsonProvider(new StaticJsonProviderOptions(document.RootElement.Clone()));
        var query = new StaticJsonProviderQueryOptions();

        var first = await provider.FetchConfigurationBytesAsync(query);
        Array.Clear(first, 0, first.Length);

        var second = await provider.FetchConfigurationBytesAsync(query);

        Assert.Equal("""{"Region":"eu"}""", System.Text.Encoding.UTF8.GetString(second));
    }
}
