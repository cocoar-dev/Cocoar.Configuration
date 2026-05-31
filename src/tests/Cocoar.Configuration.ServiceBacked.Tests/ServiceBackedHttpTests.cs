using System.Net;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Http;       // FromHttp((sp,a)=>HttpClient, ...)
using Cocoar.Configuration.Providers;  // FromStaticJson
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// ADR-006 headline #1: a global service-backed (Layer-2) HTTP rule sources its <c>HttpClient</c> from the
/// container's <c>IHttpClientFactory</c>, and the value arrives after host start.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedHttpTests
{
    private static HostApplicationBuilder NewHostWithClient(string clientName, StubHttpHandler handler)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddHttpClient(clientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return builder;
    }

    [Fact]
    public async Task GlobalHttp_ViaHttpClientFactory_LandsAfterHostStart()
    {
        var handler = new StubHttpHandler("""{ "Value": "from-remote" }""");
        var builder = NewHostWithClient("cocoar-config", handler);

        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cocoar-config"),
                    "https://config.example/remote.json"),
            ])
            .UseDebounce(25));

        using var host = builder.Build();
        var mgr = host.Services.GetRequiredService<ConfigManager>();

        // Before host start: the sp-gated Layer-2 rule is dormant → the Layer-1 base is visible.
        Assert.Equal("base", mgr.GetConfig<RemoteConfig>()!.Value);

        await host.StartAsync();

        // After host start: Layer 2 activated and merged over Layer 1 — the IHttpClientFactory client was used.
        Assert.Equal("from-remote", mgr.GetConfig<RemoteConfig>()!.Value);
        Assert.True(handler.CallCount >= 1);

        await host.StopAsync();
    }

    [Fact]
    public async Task Layer2OnlyType_IsUnresolvedBeforeStart_ResolvedAfter()
    {
        var handler = new StubHttpHandler("""{ "Value": "remote-only" }""");
        var builder = NewHostWithClient("cfg", handler);

        builder.Services.AddCocoarConfiguration(c => c
            // RemoteConfig has NO Layer-1 rule — it exists only in Layer 2.
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg"),
                    "https://x/remote.json"),
            ])
            .UseDebounce(25));

        using var host = builder.Build();
        var mgr = host.Services.GetRequiredService<ConfigManager>();

        // Readiness (ADR-006 §7): a type that exists only in Layer 2 is unresolved before host start —
        // TryGetConfig returns false (and GetConfig would throw), matching "unresolved (null)".
        Assert.False(mgr.TryGetConfig<RemoteConfig>(out _));

        await host.StartAsync();

        Assert.True(mgr.TryGetConfig<RemoteConfig>(out var after));
        Assert.Equal("remote-only", after!.Value);

        await host.StopAsync();
    }

    [Fact]
    public async Task FailingOptionalLayer2_RollsBackToLayer1_AndDegradesHealth()
    {
        // The remote source is down (HTTP 500) → EnsureSuccessStatusCode throws → optional rule degrades.
        var handler = new StubHttpHandler(_ => (HttpStatusCode.InternalServerError, "boom"));
        var builder = NewHostWithClient("cfg", handler);

        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg"),
                    "https://x/remote.json"),
            ])
            .UseDebounce(25));

        using var host = builder.Build();

        // Host start must NOT fault on a remote outage.
        await host.StartAsync();

        var mgr = host.Services.GetRequiredService<ConfigManager>();

        // Layer-1 value persists; health is degraded (the failed optional rule is recorded).
        Assert.Equal("base", mgr.GetConfig<RemoteConfig>()!.Value);
        Assert.False(mgr.IsHealthy);

        await host.StopAsync();
    }

    [Fact]
    public async Task Layer2OnlyType_PassesValidateOnBuildAndScopes()
    {
        // ASP.NET Core's Development default turns on ValidateOnBuild + ValidateScopes. A Layer-2-only type is
        // unresolved before host start; this guards that build/validation never invokes the (factory-registered)
        // config services, so an all-remote config type does not break host construction.
        var handler = new StubHttpHandler("""{ "Value": "remote-only" }""");

        using var host = new HostBuilder()
            .UseDefaultServiceProvider((_, options) =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureServices(services =>
            {
                services.AddHttpClient("cfg").ConfigurePrimaryHttpMessageHandler(() => handler);
                services.AddCocoarConfiguration(c => c
                    .UseServiceBackedConfiguration(rules =>
                    [
                        rules.For<RemoteConfig>().FromHttp(
                            (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg"),
                            "https://x/remote.json"),
                    ])
                    .UseDebounce(25));
            })
            .Build(); // must not throw under ValidateOnBuild

        await host.StartAsync();

        var mgr = host.Services.GetRequiredService<ConfigManager>();
        Assert.Equal("remote-only", mgr.GetConfig<RemoteConfig>()!.Value);

        await host.StopAsync();
    }
}
