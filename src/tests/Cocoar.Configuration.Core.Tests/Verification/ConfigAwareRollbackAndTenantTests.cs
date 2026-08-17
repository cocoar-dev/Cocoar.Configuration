using System.Reactive.Subjects;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Verification;

/// <summary>
/// Rule factories resolve the in-flight pass rather than the last committed snapshot. That raises two questions
/// these tests answer: whether a pass that rolls back can leave a derived rule holding the value it computed
/// from uncommitted state, and whether a tenant pipeline follows its base the same way the global one does.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "ConfigAware")]
public class ConfigAwareRollbackAndTenantTests
{
    public class SourceCfg { public string Name { get; set; } = "unset"; }

    public class DerivedCfg { public string Value { get; set; } = "unset"; }

    public class StrictCfg { public string Value { get; set; } = "unset"; }

    /// <summary>
    /// A required rule fails AFTER a derived rule already computed from the in-flight values. The whole pass must
    /// roll back: neither the source nor the derived value may advance, and the derived rule must not keep the
    /// value it produced from the discarded state — the next healthy pass has to correct it.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task FailedPass_DoesNotPublishValuesDerivedFromDiscardedState()
    {
        using var source = new BehaviorSubject<string>("""{"Name":"one"}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<DerivedCfg>().FromStatic(a =>
                new DerivedCfg { Value = "d-" + a.GetConfig<SourceCfg>()!.Name }),
            builder.For<StrictCfg>().FromStatic(a =>
                a.GetConfig<SourceCfg>()!.Name == "poison"
                    ? throw new InvalidOperationException("simulated required-rule failure")
                    : new StrictCfg { Value = "s-" + a.GetConfig<SourceCfg>()!.Name })
                .Required(),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));

        Assert.Equal("one", mgr.GetConfig<SourceCfg>()!.Name);
        Assert.Equal("d-one", mgr.GetConfig<DerivedCfg>()!.Value);
        Assert.Equal("s-one", mgr.GetConfig<StrictCfg>()!.Value);

        // This pass computes a derived value from in-flight state and then fails on the required rule.
        source.OnNext("""{"Name":"poison"}""");
        await Task.Delay(500);

        Assert.Equal("one", mgr.GetConfig<SourceCfg>()!.Name);
        Assert.Equal("d-one", mgr.GetConfig<DerivedCfg>()!.Value);
        Assert.Equal("s-one", mgr.GetConfig<StrictCfg>()!.Value);

        // The engine must not be poisoned by the discarded pass: a healthy change still lands completely.
        source.OnNext("""{"Name":"two"}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<DerivedCfg>()!.Value == "d-two",
            description: "recovery pass to land");

        Assert.Equal("two", mgr.GetConfig<SourceCfg>()!.Name);
        Assert.Equal("d-two", mgr.GetConfig<DerivedCfg>()!.Value);
        Assert.Equal("s-two", mgr.GetConfig<StrictCfg>()!.Value);
    }

    /// <summary>
    /// A tenant pipeline runs the same flat rule list against its own state, so a tenant-scoped derived rule has
    /// to follow a change in the shared base exactly like the global pipeline does.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task TenantScopedDerivedRule_FollowsTheSharedBase()
    {
        using var source = new BehaviorSubject<string>("""{"Name":"one"}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<DerivedCfg>().FromStatic(a =>
                new DerivedCfg { Value = $"{a.Tenant}-{a.GetConfig<SourceCfg>()!.Name}" }).TenantScoped(),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
        await mgr.InitializeTenantAsync("acme");
        await mgr.InitializeTenantAsync("globex");

        Assert.Equal("acme-one", mgr.GetConfigForTenant<DerivedCfg>("acme")!.Value);
        Assert.Equal("globex-one", mgr.GetConfigForTenant<DerivedCfg>("globex")!.Value);

        source.OnNext("""{"Name":"two"}""");

        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfigForTenant<DerivedCfg>("acme")!.Value == "acme-two"
                && mgr.GetConfigForTenant<DerivedCfg>("globex")!.Value == "globex-two",
            timeout: TimeSpan.FromSeconds(5),
            description: "both tenants to follow the base change");

        Assert.Equal("acme-two", mgr.GetConfigForTenant<DerivedCfg>("acme")!.Value);
        Assert.Equal("globex-two", mgr.GetConfigForTenant<DerivedCfg>("globex")!.Value);
    }
}
