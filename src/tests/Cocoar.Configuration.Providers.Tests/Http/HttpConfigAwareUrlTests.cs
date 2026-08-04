using System.Net;
using System.Reactive.Subjects;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Http;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests.Http;

/// <summary>
/// The headline example of guide/configuration/config-aware.md: a poll URL derived from an upstream config.
/// When the upstream region changes the provider has to switch endpoints, in the same way a derived file path
/// switches files — the provider behind a rule must not change the semantics.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "ConfigAware")]
public class HttpConfigAwareUrlTests
{
    public class RegionCfg { public string Region { get; set; } = "unset"; }

    public class ApiCfg { public string Value { get; set; } = "unset"; }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task DerivedUrl_SwitchesEndpointWhenItsSourceChanges()
    {
        var handler = new RegionRoutingHandler();
        using var source = new BehaviorSubject<string>("""{"Region":"us"}""");

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules =>
        [
            rules.For<RegionCfg>().FromObservable(source),
            rules.For<ApiCfg>().FromHttp(a => new(
                url: $"https://example.com/{a.GetConfig<RegionCfg>()!.Region}/config",
                pollInterval: TimeSpan.FromMilliseconds(50),
                handler: handler)),
        ]).UseDebounce(50));

        Assert.Equal("US", mgr.GetConfig<ApiCfg>()!.Value);

        source.OnNext("""{"Region":"eu"}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && mgr.GetConfig<ApiCfg>()!.Value != "EU")
        {
            await Task.Delay(40);
        }

        Assert.Equal("eu", mgr.GetConfig<RegionCfg>()!.Region);
        Assert.Equal("EU", mgr.GetConfig<ApiCfg>()!.Value);
    }

    private sealed class RegionRoutingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var region = request.RequestUri!.AbsolutePath.Contains("/eu/", StringComparison.Ordinal) ? "EU" : "US";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"Value":"{{region}}"}"""),
            });
        }
    }
}
