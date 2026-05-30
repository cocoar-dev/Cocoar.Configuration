using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;          // FromStore, UseServiceBackedConfiguration
using Cocoar.Configuration.Providers;   // FromStaticJson
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// ADR-006 headline #2: <c>FromStore((sp,a)=&gt;…).TenantScoped()</c> — a DB-backed (Marten-style) source per
/// tenant. Proves the tenant gate and the sp gate compose: the rule runs only inside a tenant pipeline, post
/// host start, sourcing a backend from the DI-managed store keyed by the tenant.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Type", "Unit")]
public class ServiceBackedStorageTests
{
    [Fact]
    public async Task MartenPerTenant_ComposesTenantGateAndServiceProviderGate()
    {
        var store = new FakeDocumentStore();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IFakeDocumentStore>(store);
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                // A non-tenant global base so the type stays injectable and the global pipeline has a value.
                rules.For<TenantSettings>().FromStaticJson("""{ "Db": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<TenantSettings>()
                    .FromStore((sp, a) => sp.GetRequiredService<IFakeDocumentStore>().BackendFor(a.Tenant))
                    .TenantScoped(),
            ])
            .UseDebounce(25));

        using var host = builder.Build();
        await host.StartAsync();

        var mgr = host.Services.GetRequiredService<ConfigManager>();
        var tenants = (ITenantConfigurationAccessor)mgr;

        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("globex");

        await Wait.UntilAsync(() => mgr.GetConfigForTenant<TenantSettings>("acme")?.Db == "db-for-acme", "acme tenant value");
        await Wait.UntilAsync(() => mgr.GetConfigForTenant<TenantSettings>("globex")?.Db == "db-for-globex", "globex tenant value");

        // Each tenant got its OWN backend (the sp + a.Tenant were both used).
        Assert.Equal("db-for-acme", mgr.GetConfigForTenant<TenantSettings>("acme")!.Db);
        Assert.Equal("db-for-globex", mgr.GetConfigForTenant<TenantSettings>("globex")!.Db);
        Assert.Contains("acme", store.RequestedTenants);
        Assert.Contains("globex", store.RequestedTenants);

        // The global (tenant-agnostic) pipeline skipped the tenant-scoped Layer-2 rule → only the base shows.
        Assert.Equal("base", mgr.GetConfig<TenantSettings>()!.Db);
        Assert.DoesNotContain("", store.RequestedTenants); // global pipeline never invoked the factory

        await host.StopAsync();
    }

    [Fact]
    public async Task TenantInitializedBeforeActivation_SeesLayer1Base_NotServiceBackedValue()
    {
        var store = new FakeDocumentStore();

        // A plain container that is never activated stands in for "before host start": the sp is never published.
        var services = new ServiceCollection();
        services.AddSingleton<IFakeDocumentStore>(store);
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<TenantSettings>().FromStaticJson("""{ "Db": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<TenantSettings>()
                    .FromStore((sp, a) => sp.GetRequiredService<IFakeDocumentStore>().BackendFor(a.Tenant))
                    .TenantScoped(),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();
        var tenants = (ITenantConfigurationAccessor)mgr;

        // The sp gate keeps Layer 2 dormant, so the tenant sees the Layer-1 base and the store is never touched.
        await tenants.InitializeTenantAsync("acme");
        Assert.Equal("base", mgr.GetConfigForTenant<TenantSettings>("acme")!.Db);
        Assert.Empty(store.RequestedTenants);
    }
}
