namespace Cocoar.Configuration.Core.Tests.Integration;

/// <summary>
/// Pins the observable behavior of the case-insensitive layer merge (ConfigMergeOptions.CaseInsensitive),
/// added in PR #50 and first shipping in v6.0.0. Verifies it before release — especially the dictionary case.
/// </summary>
public class CaseInsensitiveLayerMergeTests
{
    public sealed class ProbeConfig
    {
        public int Port { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string> Map { get; set; } = new();
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void PocoProperty_CaseVariantAcrossLayers_HigherLayerWins_AndOtherKeysSurvive()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<ProbeConfig>().FromStaticJson("""{ "port": 25, "name": "base" }"""),
                rules.For<ProbeConfig>().FromStaticJson("""{ "Port": 587 }"""),
            ]));

        var cfg = manager.GetConfig<ProbeConfig>()!;

        Assert.Equal(587, cfg.Port);       // "Port" overrides "port" (case-insensitive)
        Assert.Equal("base", cfg.Name);    // untouched lower-layer key survives (deep merge)
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Value_CasingIsNeverTouched()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<ProbeConfig>().FromStaticJson("""{ "Name": "Hello-WORLD" }"""),
            ]));

        Assert.Equal("Hello-WORLD", manager.GetConfig<ProbeConfig>()!.Name);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Dictionary_DistinctKeysAcrossLayers_DeepMergeKeepsBoth()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<ProbeConfig>().FromStaticJson("""{ "Map": { "Foo": "1" } }"""),
                rules.For<ProbeConfig>().FromStaticJson("""{ "Map": { "Bar": "2" } }"""),
            ]));

        var map = manager.GetConfig<ProbeConfig>()!.Map;

        // Reveals whether nested objects deep-merge (Foo + Bar) or the higher layer replaces (Bar only).
        Assert.Equal(2, map.Count);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Dictionary_CaseDistinctKeysAcrossLayers_RevealsCollapseBehavior()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<ProbeConfig>().FromStaticJson("""{ "Map": { "X-Trace": "a" } }"""),
                rules.For<ProbeConfig>().FromStaticJson("""{ "Map": { "x-trace": "b" } }"""),
            ]));

        var map = manager.GetConfig<ProbeConfig>()!.Map;

        // CONFIRMED: the case-insensitive merge collapses case-distinct dictionary keys across layers into one.
        // The result is a MIX: the higher layer wins the VALUE ("b"), but the LOWER layer's KEY casing
        // ("X-Trace") is retained — so the consumer sees neither layer's entry verbatim.
        Assert.Equal(1, map.Count);
        Assert.Equal("b", map.Single().Value);
        Assert.Equal("X-Trace", map.Single().Key);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Dictionary_CaseDistinctKeysWithinOneLayer_RevealsParseBehavior()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<ProbeConfig>().FromStaticJson("""{ "Map": { "Key": "1", "key": "2" } }"""),
            ]));

        var map = manager.GetConfig<ProbeConfig>()!.Map;

        // CONFIRMED (the asymmetry): a SINGLE layer PRESERVES case-distinct dictionary keys (ordinal JSON parse),
        // even though the cross-layer MERGE above collapses them. Same data, different result depending on
        // whether it arrives in one layer or is split across layers.
        Assert.Equal(2, map.Count);
    }
}
