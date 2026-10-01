using Xunit;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI.Tests;

public class DiFeatureConfig
{
    public bool NewCheckoutEnabled { get; set; }
}

public class DiTenantConfig
{
    public bool AllowExperiments { get; set; }
}

/// <summary>
/// The "Multiple Config Sources" pattern from guide/flags/defining-flags.md, resolved through the container.
/// </summary>
public partial class DiRolloutFlags : IFeatureFlags<(DiFeatureConfig Features, DiTenantConfig Tenant)>
{
    public DateTimeOffset ExpiresAt => new(2099, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public bool NewCheckout() => Config.Features.NewCheckoutEnabled && Config.Tenant.AllowExperiments;
}

public partial class DiTenantEntitlements : IEntitlements<(DiFeatureConfig Features, DiTenantConfig Tenant)>
{
    public bool MayExperiment() => Config.Tenant.AllowExperiments;
}

/// <summary>
/// A flag class is registered as its own implementation type, so the container constructs it and has to resolve
/// the <c>IReactiveConfig&lt;TConfig&gt;</c> its generated constructor asks for. For a tuple config that is a
/// different service type than the per-config-type reactive registrations, which is the case this covers.
/// </summary>
public class TupleConfigFlagsDiTests
{
    [Fact]
    public void TupleConfigFlagClass_ResolvesFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<DiFeatureConfig>().FromStaticJson("""{"NewCheckoutEnabled":true}"""),
                rules.For<DiTenantConfig>().FromStaticJson("""{"AllowExperiments":true}"""),
            ])
            .UseFeatureFlags(flags => [flags.Register<DiRolloutFlags>()]));

        using var sp = services.BuildServiceProvider();

        var flags = sp.GetRequiredService<DiRolloutFlags>();

        Assert.True(flags.NewCheckout());
    }

    [Fact]
    public void TupleConfigEntitlementClass_ResolvesFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<DiFeatureConfig>().FromStaticJson("""{"NewCheckoutEnabled":false}"""),
                rules.For<DiTenantConfig>().FromStaticJson("""{"AllowExperiments":true}"""),
            ])
            .UseEntitlements(e => [e.Register<DiTenantEntitlements>()]));

        using var sp = services.BuildServiceProvider();

        Assert.True(sp.GetRequiredService<DiTenantEntitlements>().MayExperiment());
    }
}
