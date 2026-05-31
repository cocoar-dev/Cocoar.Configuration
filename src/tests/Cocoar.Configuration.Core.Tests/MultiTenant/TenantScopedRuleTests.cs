using Cocoar.Configuration.Core;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Providers; // FromObservable extension

namespace Cocoar.Configuration.Core.Tests.MultiTenant;

[Trait("Category", "MultiTenant")]
[Trait("Type", "Unit")]
public class TenantScopedRuleTests
{
    public sealed record Foo
    {
        public string Value { get; init; } = "";
    }

    /// <summary>
    /// In the global (tenant-agnostic) pipeline, <see cref="IConfigurationAccessor.Tenant"/> is null, so a
    /// <c>.TenantScoped()</c> rule must be skipped — its overlay does not apply and the base value wins.
    /// </summary>
    [Fact]
    public async Task TenantScopedRule_IsSkipped_InGlobalPipeline()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<Foo>().FromObservable("""{ "Value": "base" }"""),
                rules.For<Foo>().FromObservable("""{ "Value": "tenant-overlay" }""").TenantScoped(),
            ])
            .UseDebounce(25));

        await ActiveWaitHelpers.WaitUntilAsync(() => mgr.GetConfig<Foo>() is not null, description: "init");

        Assert.Equal("base", mgr.GetConfig<Foo>()!.Value); // tenant overlay skipped (no tenant)
    }

    [Fact]
    public void GlobalAccessor_HasNullTenant()
    {
        using var mgr = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<Foo>().FromObservable("""{ "Value": "x" }""")]));

        Assert.Null(((IConfigurationAccessor)mgr).Tenant);
    }
}
