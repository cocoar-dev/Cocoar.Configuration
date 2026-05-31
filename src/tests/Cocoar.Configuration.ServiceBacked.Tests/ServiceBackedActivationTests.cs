using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;          // FromStore, UseServiceBackedConfiguration, ActivateServiceBackedConfigurationAsync
using Cocoar.Configuration.Providers;   // FromStaticJson
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;     // IHostedService

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// Activation mechanics: manual (non-host) activation, the non-breaking guarantee (a hosted service only when
/// Layer 2 is used), the fluent-order-proof sp gate, and the misuse guardrail.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedActivationTests
{
    [Fact]
    public async Task ManualActivation_OnPlainContainer_ActivatesLayer2()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStore((_, _) => new SeededBackend("""{ "Value": "stored" }""")),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        // Before activation: dormant → Layer-1 base.
        Assert.Equal("base", mgr.GetConfig<RemoteConfig>()!.Value);

        // ActivateAsync awaits the recompute, so the value is ready synchronously after it returns.
        await sp.ActivateServiceBackedConfigurationAsync();
        Assert.Equal("stored", mgr.GetConfig<RemoteConfig>()!.Value);
    }

    [Fact]
    public async Task ManualActivation_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStore((_, _) => new SeededBackend("""{ "Value": "stored" }""")),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        await sp.ActivateServiceBackedConfigurationAsync();
        await sp.ActivateServiceBackedConfigurationAsync(); // second call is a no-op, must not throw or regress
        Assert.Equal("stored", mgr.GetConfig<RemoteConfig>()!.Value);
    }

    [Fact]
    public void Layer1Only_RegistersNoActivationHostedService()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ]));

        // Non-breaking rule 3: apps that do not opt into Layer 2 get no hosted service.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void ServiceBacked_RegistersExactlyOneActivationHostedService()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStore((_, _) => new SeededBackend("{}")),
            ]));

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task ActivationGate_SurvivesATrailingUserWhen_FluentOrderProof()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                // A user .When() AFTER the sp-overload must NOT clobber the activation gate.
                rules.For<RemoteConfig>()
                    .FromStore((_, _) => new SeededBackend("""{ "Value": "stored" }"""))
                    .When(_ => true),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        // Still dormant before activation despite .When(_ => true).
        Assert.Equal("base", mgr.GetConfig<RemoteConfig>()!.Value);

        await sp.ActivateServiceBackedConfigurationAsync();
        Assert.Equal("stored", mgr.GetConfig<RemoteConfig>()!.Value);
    }

    // NOTE: using FromStore / FromHttp((sp,a)=>…) outside UseServiceBackedConfiguration is now a COMPILE error
    // (those overloads target ServiceBackedProviderBuilder<T>, which UseConfiguration's plain
    // TypedProviderBuilder<T> is not) — there is no longer a runtime-throw path to test.
}
