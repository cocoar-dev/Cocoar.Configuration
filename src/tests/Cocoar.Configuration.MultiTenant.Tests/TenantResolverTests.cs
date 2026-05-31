using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.MultiTenant.Tests;

/// <summary>
/// The DI tenant resolver: <c>AddCocoarTenantResolver&lt;TService&gt;</c> registers a scoped
/// <c>ITenantContext</c> from the app's own tenant service, and the scoped <c>ITenantReactiveConfig&lt;T&gt;</c>
/// adapter binds to it — no hand-written adapter. (No-DI hosts resolve tenants explicitly via
/// <c>…ForTenant(id)</c>, so there is no ambient-context path there.)
/// </summary>
[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public sealed class TenantResolverTests
{
    public sealed record RegionCfg
    {
        public string Region { get; init; } = "base";
    }

    private sealed class AppTenantService
    {
        public string? TenantId { get; set; }
    }

    [Fact]
    public async Task AddCocoarTenantResolver_RegistersScopedTenantContext_FromAppService()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<RegionCfg>().FromStaticJson("""{ "Region": "base" }"""),
                rules.For<RegionCfg>().FromStatic(a => new RegionCfg { Region = $"region-{a.Tenant}" }).TenantScoped(),
            ])
            .UseDebounce(25));

        // The app already has its own tenant service — point the resolver at it, no ITenantContext to write.
        services.AddScoped<AppTenantService>();
        services.AddCocoarTenantResolver<AppTenantService>(s => s.TenantId);
        services.AddCocoarTenantReactiveConfig();

        await using var sp = services.BuildServiceProvider();
        var tenants = (ITenantConfigurationAccessor)sp.GetRequiredService<ConfigManager>();
        await tenants.InitializeTenantAsync("acme");

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppTenantService>().TenantId = "acme";

        // The resolver-provided ITenantContext reflects the app service...
        Assert.Equal("acme", scope.ServiceProvider.GetRequiredService<ITenantContext>().Current);
        // ...and the scoped reactive adapter binds to it.
        var cfg = scope.ServiceProvider.GetRequiredService<ITenantReactiveConfig<RegionCfg>>();
        Assert.Equal("region-acme", cfg.CurrentValue.Region);
    }
}
