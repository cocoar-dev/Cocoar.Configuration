using System.Reactive.Subjects;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Verification;

/// <summary>
/// Acceptance tests for behaviour the published documentation promises for config-aware rules
/// (guide/configuration/config-aware.md, "Re-evaluation on Change"). Between v4.2.0 and v6.1.0 a
/// derived value froze or lagged a recompute behind and a derived file path never followed its
/// source at all; the two rule-order tests below lock the repaired behaviour in.
/// <para>
/// Background, measured traces and root-cause analysis: Atlas topic
/// <c>Cocoar.Configuration / config-aware-rules-runtime-staleness</c>.
/// </para>
/// <para>
/// The <c>Probe*</c> tests are diagnostics, not assertions — they end in <c>Assert.Fail</c> to print
/// a trace of what the engine actually did, so they stay skipped.
/// <c>ChangingOneType_DoesNotEmitForAnUnchangedType</c> covers a separate, still-open finding.
/// </para>
/// </summary>
[Trait("Category", "Verification")]
public class ConfigAwareRuntimeStalenessTests
{
    public class SourceCfg
    {
        public string Region { get; set; } = "unset";
        public string Name { get; set; } = "unset";
    }

    public class DerivedCfg { public string Endpoint { get; set; } = "unset"; }

    public class TargetCfg { public string Value { get; set; } = "unset"; }

    public class OtherCfg { public string Value { get; set; } = "unset"; }

    // ---------------------------------------------------------------- acceptance

    /// <summary>
    /// website/guide/configuration/config-aware.md, "Re-evaluation on Change": the factory is
    /// re-evaluated per recompute and "reads the new region".
    /// Observed: on a pristine 6c79b28 the derived value freezes; with a content-derived
    /// StaticJson provider key it lags exactly one recompute behind.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task DerivedStaticValue_FollowsItsSource()
    {
        using var source = new BehaviorSubject<string>("""{"Region":"us"}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<DerivedCfg>().FromStatic(a =>
                new DerivedCfg { Endpoint = "db-" + a.GetConfig<SourceCfg>()!.Region }),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
        Assert.Equal("db-us", mgr.GetConfig<DerivedCfg>()!.Endpoint);

        source.OnNext("""{"Region":"eu"}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<SourceCfg>()!.Region == "eu", description: "source to become eu");
        await Task.Delay(300);

        Assert.Equal("db-eu", mgr.GetConfig<DerivedCfg>()!.Endpoint);
    }

    /// <summary>
    /// Same doc page, "Dynamic file paths": a derived path must switch the rule to the new file.
    /// Observed: never updates — the file name lives in the query options, which neither rebuild
    /// the provider nor invalidate the rule's transform cache.
    /// </summary>
    [Fact]
    [Trait("Type", "Unit")]
    public async Task DerivedFilePath_FollowsItsSource()
    {
        var dir = CreateProbeDirectory();
        try
        {
            using var source = new BehaviorSubject<string>("""{"Name":"a"}""");
            var builder = new RulesBuilder();

            var rules = new List<ConfigRule>
            {
                TestRules.ObservableString<SourceCfg>(source),
                builder.For<TargetCfg>().FromFile(a =>
                    FileSourceRuleOptions.FromFilePath(
                        Path.Combine(dir, a.GetConfig<SourceCfg>()!.Name + ".json"))),
            };

            using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));
            Assert.Equal("A", mgr.GetConfig<TargetCfg>()!.Value);

            source.OnNext("""{"Name":"b"}""");
            await ActiveWaitHelpers.WaitUntilAsync(
                () => mgr.GetConfig<SourceCfg>()!.Name == "b", description: "source to become b");
            await Task.Delay(400);

            Assert.Equal("B", mgr.GetConfig<TargetCfg>()!.Value);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// website/guide/reactive/basics.md:76 — "If the data hasn't changed (same JSON content), the
    /// same instance reference is reused and the subscriber is not called."
    /// Observed: changing only type A also emits for type B and gives B a fresh instance, because
    /// every commit re-deserializes every type. Impact is spurious wakeups, not wrong values.
    /// </summary>
    [Fact(Skip = "Known failing — documents the per-type emission gap. Remove Skip to reproduce.")]
    [Trait("Type", "Unit")]
    public async Task ChangingOneType_DoesNotEmitForAnUnchangedType()
    {
        using var subjA = new BehaviorSubject<string>("""{"Value":"a1"}""");
        using var subjB = new BehaviorSubject<string>("""{"Value":"b1"}""");

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<TargetCfg>(subjA),
            TestRules.ObservableString<OtherCfg>(subjB),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));

        var emissionsB = new List<OtherCfg>();
        using var sub = mgr.GetReactiveConfig<OtherCfg>().Subscribe(x => emissionsB.Add(x));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissionsB.Count > 0, description: "initial emission for B");

        var emissionsBefore = emissionsB.Count;
        var refBefore = mgr.GetConfig<OtherCfg>();

        subjA.OnNext("""{"Value":"a2"}""");
        await ActiveWaitHelpers.WaitUntilAsync(
            () => mgr.GetConfig<TargetCfg>()!.Value == "a2", description: "A to update");
        await Task.Delay(300);

        Assert.True(
            emissionsBefore == emissionsB.Count,
            $"B emitted although only A changed: before={emissionsBefore}, after={emissionsB.Count}");
        Assert.Same(refBefore, mgr.GetConfig<OtherCfg>());
    }

    // ---------------------------------------------------------------- trace probes

    /// <summary>
    /// Prints how a derived static value evolves across several source changes. Distinguishes
    /// "frozen forever" from "lags exactly one recompute".
    /// </summary>
    [Fact(Skip = "Trace probe, not an assertion. Remove Skip to print the observed behaviour.")]
    [Trait("Type", "Unit")]
    public async Task ProbeDerivedStaticLag()
    {
        using var source = new BehaviorSubject<string>("""{"Region":"us"}""");
        var builder = new RulesBuilder();

        var rules = new List<ConfigRule>
        {
            TestRules.ObservableString<SourceCfg>(source),
            builder.For<DerivedCfg>().FromStatic(a =>
                new DerivedCfg { Endpoint = "db-" + a.GetConfig<SourceCfg>()!.Region }),
        };

        using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));

        var trace = new List<string>
        {
            $"start: src={mgr.GetConfig<SourceCfg>()!.Region} derived={mgr.GetConfig<DerivedCfg>()!.Endpoint}",
        };

        foreach (var region in new[] { "eu", "ap", "sa" })
        {
            source.OnNext($$"""{"Region":"{{region}}"}""");
            await ActiveWaitHelpers.WaitUntilAsync(
                () => mgr.GetConfig<SourceCfg>()!.Region == region, description: region);
            await Task.Delay(300);
            trace.Add($"after {region}: src={mgr.GetConfig<SourceCfg>()!.Region} derived={mgr.GetConfig<DerivedCfg>()!.Endpoint}");
        }

        Assert.Fail("TRACE >>> " + string.Join(" | ", trace));
    }

    /// <summary>
    /// Prints how a derived file path evolves across several source changes.
    /// </summary>
    [Fact(Skip = "Trace probe, not an assertion. Remove Skip to print the observed behaviour.")]
    [Trait("Type", "Unit")]
    public async Task ProbeDerivedFilePath()
    {
        var dir = CreateProbeDirectory();
        try
        {
            using var source = new BehaviorSubject<string>("""{"Name":"a"}""");
            var builder = new RulesBuilder();

            var rules = new List<ConfigRule>
            {
                TestRules.ObservableString<SourceCfg>(source),
                builder.For<TargetCfg>().FromFile(a =>
                    FileSourceRuleOptions.FromFilePath(
                        Path.Combine(dir, a.GetConfig<SourceCfg>()!.Name + ".json"))),
            };

            using var mgr = ConfigManager.Create(c => c.UseConfiguration(rules).UseDebounce(50));

            var trace = new List<string>
            {
                $"start: src={mgr.GetConfig<SourceCfg>()!.Name} target={mgr.GetConfig<TargetCfg>()!.Value}",
            };

            foreach (var name in new[] { "b", "c" })
            {
                source.OnNext($$"""{"Name":"{{name}}"}""");
                await ActiveWaitHelpers.WaitUntilAsync(
                    () => mgr.GetConfig<SourceCfg>()!.Name == name, description: name);
                await Task.Delay(400);
                trace.Add($"after {name}: src={mgr.GetConfig<SourceCfg>()!.Name} target={mgr.GetConfig<TargetCfg>()!.Value}");
            }

            Assert.Fail("TRACE >>> " + string.Join(" | ", trace));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string CreateProbeDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cocoar-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.json"), """{"Value":"A"}""");
        File.WriteAllText(Path.Combine(dir, "b.json"), """{"Value":"B"}""");
        File.WriteAllText(Path.Combine(dir, "c.json"), """{"Value":"C"}""");
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Probe artefact in the temp folder — losing it is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
