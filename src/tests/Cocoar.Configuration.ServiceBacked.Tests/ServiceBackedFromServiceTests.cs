using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers; // FromStaticJson
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.ServiceBacked.Tests;

public sealed record AppCfg
{
    public string Value { get; init; } = "";
}

/// <summary>A DI service that holds the configuration in memory (e.g. computed/derived at startup).</summary>
internal sealed class AppCfgSource
{
    public AppCfg Settings { get; } = new() { Value = "from-service" };
}

/// <summary>
/// <c>FromService&lt;TService&gt;(s =&gt; s.Settings)</c> — derive config from a single DI service (Cocoar's
/// analog of <c>services.Configure&lt;TDep&gt;((opts, dep) =&gt; …)</c>). No custom provider needed.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedFromServiceTests
{
    [Fact]
    public async Task FromService_ResolvesServiceAndProducesConfig_OnActivation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AppCfgSource>();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<AppCfg>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                // The exact requested shape: TService explicit, the service handed to the lambda.
                rules.For<AppCfg>().FromService<AppCfgSource>(s => s.Settings),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        // Dormant before activation → Layer-1 base.
        Assert.Equal("base", mgr.GetConfig<AppCfg>()!.Value);

        await sp.ActivateServiceBackedConfigurationAsync();

        // Activated → resolved from the DI service.
        Assert.Equal("from-service", mgr.GetConfig<AppCfg>()!.Value);
    }

    [Fact]
    public async Task FromService_UnregisteredService_DoesNotCrashActivation_Layer1Persists()
    {
        // AppCfgSource is NOT registered → resolution throws at recompute → the recompute rolls back and the
        // Layer-1 base persists; activation must not fault.
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<AppCfg>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<AppCfg>().FromService<AppCfgSource>(s => s.Settings),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        await sp.ActivateServiceBackedConfigurationAsync(); // must not throw
        Assert.Equal("base", mgr.GetConfig<AppCfg>()!.Value);
    }
}
