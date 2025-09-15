using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Tests;

public class TryGetAndStaticSeedingTests
{
    public class SeededSettings
    {
        public string Name { get; set; } = "x";
        public int Value { get; set; } = 1;
    }

    public class Outer
    {
        public SeededSettings? Inner { get; set; }
    }

    public class Container
    {
        public SeededSettings? Dep { get; set; }
    }

    [Fact]
    public void TryGetConfig_ReturnsFalse_WhenMissing()
    {
        var mgr = new ConfigManager(Array.Empty<ConfigRule>(), NullLogger.Instance).Initialize();
        var ok = mgr.TryGetConfig<SeededSettings>(out var s);
        Assert.False(ok);
        Assert.Null(s);
    }

    [Fact]
    public void FromStatic_Seeds_Config_And_TryGetConfig_Succeeds()
    {
        var json = JsonSerializer.SerializeToElement(new SeededSettings { Name = "A", Value = 42 });
        var rule = StaticJsonProvider.CreateRule<SeededSettings>(json);
        var mgr = new ConfigManager(new[] { rule }, NullLogger.Instance).Initialize();

        var ok = mgr.TryGetConfig<SeededSettings>(out var s);
        Assert.True(ok);
        Assert.NotNull(s);
        Assert.Equal("A", s.Name);
        Assert.Equal(42, s.Value);
    }

    [Fact]
    public void FromStatic_With_MountPath_Nests_Value()
    {
        var rule = Rule.From.Static<SeededSettings>(_ => new SeededSettings { Name = "B", Value = 7 })
            .MountAt("Inner")
            .For<Outer>()
            .Build();

        var mgr = new ConfigManager(new[] { rule }, NullLogger.Instance).Initialize();
        var outer = mgr.GetRequiredConfig<Outer>();
        Assert.NotNull(outer.Inner);
        Assert.Equal("B", outer.Inner!.Name);
        Assert.Equal(7, outer.Inner.Value);
    }

    [Fact]
    public void FromStatic_DependentRuleReadsSeededType_Succeeds()
    {
        var seedRule = Rule.From.Static<SeededSettings>(_ => new SeededSettings { Name = "Seed", Value = 11 })
            .For<SeededSettings>()
            .Build();

        var dependentRule = Rule.From.Static<SeededSettings>(cm => cm.GetRequiredConfig<SeededSettings>())
            .MountAt("Dep")
            .For<Container>()
            .Build();

        var mgr = new ConfigManager(new[] { seedRule, dependentRule }, NullLogger.Instance).Initialize();
        var c = mgr.GetRequiredConfig<Container>();
        Assert.NotNull(c.Dep);
        Assert.Equal("Seed", c.Dep!.Name);
        Assert.Equal(11, c.Dep.Value);
    }

    [Fact]
    public void FromStatic_AbsentSeed_GetRequired_Throws()
    {
        var dependentRule = Rule.From.Static<SeededSettings>(cm => cm.GetRequiredConfig<SeededSettings>())
            .MountAt("Dep")
            .For<Container>()
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ConfigManager(new[] { dependentRule }, NullLogger.Instance).Initialize());
    }
}
