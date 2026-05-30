using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Http;       // FromHttp((sp,a)=>HttpClient, ...)
using Cocoar.Configuration.Providers;  // FromStaticJson
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// Regression tests for defects found by the ADR-006 adversarial review: provider-key collision across distinct
/// IHttpClientFactory clients, per-fetch client acquisition (handler rotation), pre-activation tenant recovery,
/// scoped-provider root capture, and concurrent-activation readiness.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedReviewRegressionTests
{
    // Finding 2: two service-backed HTTP rules with DIFFERENT clients but identical poll settings must NOT collapse
    // onto one shared provider (ClientFactory is [JsonIgnore]'d, so the key must be null = non-shareable).
    [Fact]
    public async Task TwoServiceBackedHttpRules_WithDistinctClients_DoNotCollapse()
    {
        var handlerA = new StubHttpHandler("""{ "Value": "from-A" }""");
        var handlerB = new StubHttpHandler("""{ "Value": "from-B" }""");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHttpClient("A").ConfigurePrimaryHttpMessageHandler(() => handlerA);
        builder.Services.AddHttpClient("B").ConfigurePrimaryHttpMessageHandler(() => handlerB);
        builder.Services.AddCocoarConfiguration(c => c
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<ConfigA>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("A"), "https://x/a.json"),
                rules.For<ConfigB>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("B"), "https://x/b.json"),
            ])
            .UseDebounce(25));

        using var host = builder.Build();
        await host.StartAsync();
        var mgr = host.Services.GetRequiredService<ConfigManager>();

        Assert.Equal("from-A", mgr.GetConfig<ConfigA>()!.Value);
        Assert.Equal("from-B", mgr.GetConfig<ConfigB>()!.Value);
        // Both clients were actually used — proves the providers did not collapse (handlerB.CallCount would be 0).
        Assert.True(handlerA.CallCount >= 1);
        Assert.True(handlerB.CallCount >= 1);

        await host.StopAsync();
    }

    // Finding 8: the IHttpClientFactory factory is invoked per fetch (so handler rotation can apply), not cached once.
    [Fact]
    public async Task ServiceBackedHttp_AcquiresClientPerFetch()
    {
        var handler = new StubHttpHandler("""{ "Value": "v" }""");
        var factoryCalls = 0;

        var services = new ServiceCollection();
        services.AddHttpClient("cfg").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddCocoarConfiguration(c => c
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromHttp(
                    (sp, _) =>
                    {
                        Interlocked.Increment(ref factoryCalls);
                        return sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg");
                    },
                    "https://x/r.json", pollInterval: TimeSpan.FromMilliseconds(80)),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        await sp.ActivateServiceBackedConfigurationAsync();

        // Activation fetch (1) + subsequent polls (2, 3, …) each re-acquire the client.
        await Wait.UntilAsync(() => Volatile.Read(ref factoryCalls) >= 3, "client factory invoked per fetch");
        Assert.True(Volatile.Read(ref factoryCalls) >= 3);
    }

    // Findings 4/5/7: a tenant initialized BEFORE activation must recover its service-backed value once activated
    // (the activation fan-out recomputes already-initialized tenant pipelines).
    [Fact]
    public async Task TenantInitializedBeforeActivation_RecoversServiceBackedValue_AfterActivation()
    {
        var store = new FakeDocumentStore();

        var services = new ServiceCollection();
        services.AddSingleton<IFakeDocumentStore>(store);
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<TenantSettings>().FromStaticJson("""{ "Db": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<TenantSettings>()
                    .FromStorage((sp, a) => sp.GetRequiredService<IFakeDocumentStore>().BackendFor(a.Tenant))
                    .TenantScoped(),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();
        var tenants = (ITenantConfigurationAccessor)mgr;

        await tenants.InitializeTenantAsync("acme");
        Assert.Equal("base", mgr.GetConfigForTenant<TenantSettings>("acme")!.Db); // dormant before activation

        await sp.ActivateServiceBackedConfigurationAsync();

        // The activation awaited the tenant fan-out, so the value is committed synchronously after it returns.
        Assert.Equal("db-for-acme", mgr.GetConfigForTenant<TenantSettings>("acme")!.Db);
        Assert.Contains("acme", store.RequestedTenants);
    }

    // Finding 6: activating from a SCOPED provider must capture the root (via RootServiceProviderAccessor); after
    // the scope disposes, later polls must still resolve services and keep landing values (no ObjectDisposedException).
    [Fact]
    public async Task ManualActivation_FromScopedProvider_CapturesRoot_SurvivesScopeDisposal()
    {
        var handler = new StubHttpHandler("""{ "Value": "polled" }""");

        var services = new ServiceCollection();
        services.AddHttpClient("cfg").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddCocoarConfiguration(c => c
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromHttp(
                    (sp, _) => sp.GetRequiredService<IHttpClientFactory>().CreateClient("cfg"),
                    "https://x/r.json", pollInterval: TimeSpan.FromMilliseconds(80)),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.ActivateServiceBackedConfigurationAsync();
        } // scope disposed — a captured scope would now fault every poll's GetRequiredService<IHttpClientFactory>()

        Assert.Equal("polled", mgr.GetConfig<RemoteConfig>()!.Value);

        // A poll AFTER scope disposal must still reach the handler — proves the holder captured root, not the scope.
        var callsAfterActivation = handler.CallCount;
        await Wait.UntilAsync(() => handler.CallCount > callsAfterActivation, "a poll succeeds after scope disposal");
        Assert.True(mgr.IsHealthy);
    }

    // Findings 1/3: two concurrent activations both await the SAME activation recompute and observe Layer 2
    // committed (the CAS loser does not return early before the recompute completes).
    [Fact]
    public async Task ConcurrentActivations_BothObserveCommittedLayer2()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [ rules.For<RemoteConfig>().FromStaticJson("""{ "Value": "base" }""") ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteConfig>().FromStorage((_, _) => new SeededBackend("""{ "Value": "stored" }""")),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        await Task.WhenAll(
            sp.ActivateServiceBackedConfigurationAsync(),
            sp.ActivateServiceBackedConfigurationAsync());

        Assert.Equal("stored", mgr.GetConfig<RemoteConfig>()!.Value);
    }
}
