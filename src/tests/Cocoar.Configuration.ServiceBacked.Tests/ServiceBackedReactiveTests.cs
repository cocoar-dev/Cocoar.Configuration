using System.Linq;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Http;       // FromHttp((sp,a)=>HttpClient, ...)
using Cocoar.Configuration.Providers;  // FromStaticJson
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// ADR-006 §6 (the load-bearing reactive contract): Layer-2 activation is a RECOMPUTE on the existing pipeline,
/// never a rebuild — so a reactive view obtained <em>pre-container</em> (e.g. a Serilog <c>LoggingLevelSwitch</c>
/// wired before the host runs) receives the Layer-2 value when it lands, over the same live view.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedReactiveTests
{
    [Fact]
    public async Task PreContainerSubscription_ReceivesLayer2Upgrade_OnSameLiveView()
    {
        var handler = new StubHttpHandler("""{ "Level": "Debug" }""");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHttpClient("cfg").ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<LogConfig>().FromStaticJson("""{ "Level": "Info" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<LogConfig>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg"),
                    "https://x/log.json"),
            ])
            .UseDebounce(25));

        using var host = builder.Build();
        var mgr = host.Services.GetRequiredService<ConfigManager>();

        // Subscribe BEFORE the host starts — like a logging level switch wired during bootstrap.
        var observer = new CollectingObserver<LogConfig>();
        using var subscription = mgr.GetReactiveConfig<LogConfig>().Subscribe(observer);

        // Replay-1: the immediate emission is the Layer-1 file value.
        Assert.Equal(new[] { "Info" }, observer.Snapshot().Select(x => x.Level).ToArray());

        await host.StartAsync();

        // The same live view receives the Layer-2 value once activation lands.
        await Wait.UntilAsync(() => observer.Snapshot().Any(x => x.Level == "Debug"), "Layer-2 reactive upgrade");
        Assert.Equal(new[] { "Info", "Debug" }, observer.Snapshot().Select(x => x.Level).ToArray());

        await host.StopAsync();
    }
}
