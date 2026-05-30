using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;   // FromStaticJson / FromStatic
using Cocoar.Configuration.Reactive;    // IReactiveConfig<T>
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.Configuration.AspNetCore.Tests;

public sealed record TenantCfg
{
    public string Region { get; init; } = "base";
}

/// <summary>A test <see cref="ITenantContext"/> whose tenant is set per scope.</summary>
internal sealed class MutableTenantContext : ITenantContext
{
    public string? Current { get; set; }
}

/// <summary>
/// ADR-006 §11: the scoped <see cref="ITenantReactiveConfig{T}"/> adapter resolves the current request's tenant
/// config, while the singleton <see cref="IReactiveConfig{T}"/> stays the global view (the §11 trap).
/// </summary>
public class TenantReactiveConfigTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<TenantCfg>().FromStaticJson("""{ "Region": "base" }"""),
                rules.For<TenantCfg>().FromStatic(a => new TenantCfg { Region = $"region-{a.Tenant}" }).TenantScoped(),
            ])
            .UseDebounce(25));

        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());
        services.AddCocoarTenantReactiveConfig();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ScopedAdapter_BindsToCurrentRequestTenant()
    {
        await using var sp = BuildProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();
        var tenants = (ITenantConfigurationAccessor)mgr;
        await tenants.InitializeTenantAsync("acme");
        await tenants.InitializeTenantAsync("globex");

        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MutableTenantContext>().Current = "acme";
            var cfg = scope.ServiceProvider.GetRequiredService<ITenantReactiveConfig<TenantCfg>>();
            Assert.Equal("region-acme", cfg.CurrentValue.Region);
        }

        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MutableTenantContext>().Current = "globex";
            var cfg = scope.ServiceProvider.GetRequiredService<ITenantReactiveConfig<TenantCfg>>();
            Assert.Equal("region-globex", cfg.CurrentValue.Region);
        }
    }

    [Fact]
    public async Task SingletonIReactiveConfig_StaysGlobal_NotReRegisteredAsScoped()
    {
        await using var sp = BuildProvider();

        // The §11 trap: IReactiveConfig<T> is still the singleton, global (tenant-agnostic) view — resolvable
        // from the root and showing the base value, untouched by the tenant adapter registration.
        var global = sp.GetRequiredService<IReactiveConfig<TenantCfg>>();
        Assert.Equal("base", global.CurrentValue.Region);
    }

    [Fact]
    public async Task ScopedAdapter_WithoutTenant_Throws()
    {
        await using var sp = BuildProvider();

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<MutableTenantContext>().Current = null;
        var cfg = scope.ServiceProvider.GetRequiredService<ITenantReactiveConfig<TenantCfg>>();

        // Lazy: the missing tenant surfaces on first use, not at construction.
        Assert.Throws<InvalidOperationException>(() => _ = cfg.CurrentValue);
    }
}
