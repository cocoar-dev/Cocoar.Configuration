using System.Reactive.Subjects;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Verification;

/// <summary>
/// The library's promise is that the provider behind a rule does not change the semantics — every provider
/// yields JSON and the engine does the rest. These tests run the same config-aware scenario (an upstream value
/// changes at runtime, a dependent rule must follow) across the providers reachable from this project, so a
/// regression in one provider cannot hide behind another's coverage.
/// <para>The HTTP case lives in Cocoar.Configuration.Providers.Tests, which is the project that references it.</para>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "ConfigAware")]
public class ConfigAwareProviderMatrixTests
{
    public class SourceCfg
    {
        public string Name { get; set; } = "unset";
        public bool Enabled { get; set; }
    }

    public class TargetCfg { public string Value { get; set; } = "unset"; }

    public class MiddleCfg { public string Value { get; set; } = "unset"; }

    public class LeafCfg { public string Value { get; set; } = "unset"; }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task Environment_DerivedPrefix_FollowsItsSource()
    {
        var run = "MTX" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Environment.SetEnvironmentVariable($"{run}A_Value", "A");
        Environment.SetEnvironmentVariable($"{run}B_Value", "B");
        try
        {
            using var source = new BehaviorSubject<string>($$"""{"Name":"{{run}}A_"}""");
            var builder = new RulesBuilder();

            var rules = new List<ConfigRule>
            {
                TestRules.ObservableString<SourceCfg>(source),
                builder.For<TargetCfg>().FromEnvironment(a =>
                    new EnvironmentVariableRuleOptions(a.GetConfig<SourceCfg>()!.Name)),
            };

            using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
            Assert.Equal("A", mgr.GetConfig<TargetCfg>()!.Value);

            source.OnNext($$"""{"Name":"{{run}}B_"}""");
            await ActiveWaitHelpers.WaitUntilAsync(
                () => mgr.GetConfig<TargetCfg>()!.Value == "B",
                description: "environment rule to follow the derived prefix");

            Assert.Equal("B", mgr.GetConfig<TargetCfg>()!.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{run}A_Value", null);
            Environment.SetEnvironmentVariable($"{run}B_Value", null);
        }
    }

    /// <summary>
    /// <c>.When()</c> reads the same accessor as a provider factory, so a predicate must also see the pass it
    /// runs in — otherwise a rule switches on or off one recompute late.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task ConditionalRule_TogglesInTheSamePassAsItsSource()
    {
        using var source = new BehaviorSubject<string>("""{"Enabled":false}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<TargetCfg>().FromStaticJson("""{"Value":"base"}"""),
            builder.For<TargetCfg>().FromStaticJson("""{"Value":"overlay"}""")
                .When(a => a.GetConfig<SourceCfg>()!.Enabled),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
        Assert.Equal("base", mgr.GetConfig<TargetCfg>()!.Value);

        source.OnNext("""{"Enabled":true}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<TargetCfg>()!.Value == "overlay",
            description: "conditional rule to switch on");

        source.OnNext("""{"Enabled":false}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<TargetCfg>()!.Value == "base",
            description: "conditional rule to switch off again");
    }

    /// <summary>
    /// A → B → C in one rule list. Rules run in order within a pass, so a single upstream change has to reach
    /// the leaf in that same pass rather than taking one recompute per link.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task ChainedDerivations_ReachTheLeafInOnePass()
    {
        using var source = new BehaviorSubject<string>("""{"Name":"one"}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<MiddleCfg>().FromStatic(a =>
                new MiddleCfg { Value = "mid-" + a.GetConfig<SourceCfg>()!.Name }),
            builder.For<LeafCfg>().FromStatic(a =>
                new LeafCfg { Value = "leaf-" + a.GetConfig<MiddleCfg>()!.Value }),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
        Assert.Equal("leaf-mid-one", mgr.GetConfig<LeafCfg>()!.Value);

        source.OnNext("""{"Name":"two"}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<SourceCfg>()!.Name == "two",
            description: "source to update");
        await Task.Delay(300);

        Assert.Equal("mid-two", mgr.GetConfig<MiddleCfg>()!.Value);
        Assert.Equal("leaf-mid-two", mgr.GetConfig<LeafCfg>()!.Value);
    }
}
