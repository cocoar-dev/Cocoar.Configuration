using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Core.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Component", "AggregateRule")]
public class AggregateRuleIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var d in _disposables)
            try { d.Dispose(); } catch { }
        _disposables.Clear();
    }

    private ConfigManager Track(ConfigManager manager)
    {
        _disposables.Add(manager);
        return manager;
    }

    public class AppConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Env { get; set; } = string.Empty;
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_MergesSubRulesInOrder_LastWriteWins()
    {
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().Aggregate(r => [
                r.FromStaticJson("""{"Name": "Base", "Version": 1, "Env": "default"}"""),
                r.FromStaticJson("""{"Name": "Override", "Version": 2}""")
            ])
        ])));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("Override", config!.Name);   // overridden
        Assert.Equal(2, config.Version);           // overridden
        Assert.Equal("default", config.Env);       // preserved from base
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Required_AllSubRulesFail_ThrowsOnStartup()
    {
        // Required aggregate where all sub-rules fail (invalid JSON files that don't exist)
        // Using static json that produces empty, we simulate by using an aggregate
        // with sub-rules that produce empty contributions
        Assert.Throws<InvalidOperationException>(() =>
        {
            Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
            [
                rule.For<AppConfig>().Aggregate(r => [
                    r.FromFile("nonexistent-base.json"),
                    r.FromFile("nonexistent-overlay.json")
                ]).Required()
            ])));
        });
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Required_OneSubRuleSucceeds_Healthy()
    {
        // Required aggregate — at least one sub-rule contributes data.
        // The aggregate produces merged data → LastOutcome = Up → Healthy.
        // The failed optional sub-rule is absorbed inside the aggregate boundary.
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().Aggregate(r => [
                r.FromStaticJson("""{"Name": "Works", "Version": 1}"""),
                r.FromFile("nonexistent-overlay.json")  // optional, fails silently inside aggregate
            ]).Required()
        ])));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("Works", config!.Name);
        Assert.True(manager.IsHealthy);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Optional_AllSubRulesFail_Degrades()
    {
        // Optional aggregate — all sub-rules fail but system continues
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().Aggregate(r => [
                r.FromFile("nonexistent-a.json"),
                r.FromFile("nonexistent-b.json")
            ])
            // Not required — should degrade, not throw
        ])));

        // Config should be default (empty) since no rule contributed
        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Optional_InnerRequiredFails_DoesNotThrow()
    {
        // Inner sub-rule is Required but aggregate is optional — failure stays in aggregate boundary
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().FromStaticJson("""{"Name": "fallback"}"""),
            rule.For<AppConfig>().Aggregate(r => [
                r.FromFile("nonexistent.json").Required()
            ])
            // Aggregate is NOT required → inner failure is absorbed
        ])));

        var config = manager.GetConfig<AppConfig>();
        Assert.NotNull(config);
        Assert.Equal("fallback", config!.Name);
        Assert.Equal(HealthStatus.Degraded, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Required_InnerRequiredFails_Throws()
    {
        // Inner Required AND aggregate Required — failure propagates
        Assert.Throws<InvalidOperationException>(() =>
        {
            Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
            [
                rule.For<AppConfig>().Aggregate(r => [
                    r.FromFile("nonexistent.json").Required()
                ]).Required()
            ])));
        });
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_WithRegularRules_MergesCorrectly()
    {
        // Mix aggregate and regular rules for the same type
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().Aggregate(r => [
                r.FromStaticJson("""{"Name": "FromAggregate", "Version": 1}"""),
                r.FromStaticJson("""{"Env": "staging"}""")
            ]),
            // Regular rule after aggregate — should merge on top
            rule.For<AppConfig>().FromStaticJson("""{"Version": 99}""")
        ])));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("FromAggregate", config!.Name); // from aggregate
        Assert.Equal(99, config.Version);             // overridden by regular rule
        Assert.Equal("staging", config.Env);          // from aggregate sub-rule 2
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_EndToEnd_MergesLayeredConfig()
    {
        // Create temp files for layered config
        var tempDir = Path.Combine(Path.GetTempPath(), $"cocoar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var basePath = Path.Combine(tempDir, "config.json");
            var overlayPath = Path.Combine(tempDir, "config.dev.json");

            File.WriteAllText(basePath, """{"Name": "Base", "Version": 1, "Env": "production"}""");
            File.WriteAllText(overlayPath, """{"Env": "development", "Version": 2}""");

            var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
            [
                rule.For<AppConfig>()
                    .FromFiles(basePath, overlayPath)
                    .Required()
            ])));

            var config = manager.GetConfig<AppConfig>();

            Assert.NotNull(config);
            Assert.Equal("Base", config!.Name);          // from base (not overridden)
            Assert.Equal(2, config.Version);              // from overlay
            Assert.Equal("development", config.Env);      // from overlay
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void FromFiles_MissingOverlay_OptionalByDefault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cocoar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var basePath = Path.Combine(tempDir, "config.json");
            File.WriteAllText(basePath, """{"Name": "BaseOnly", "Version": 1}""");

            var missingOverlay = Path.Combine(tempDir, "config.dev.json"); // does not exist

            var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
            [
                rule.For<AppConfig>()
                    .FromFiles(basePath, missingOverlay)
                    .Required()
            ])));

            var config = manager.GetConfig<AppConfig>();

            Assert.NotNull(config);
            Assert.Equal("BaseOnly", config!.Name);
            Assert.Equal(1, config.Version);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_Health_OptionalAggregateFails_Degraded()
    {
        // Optional aggregate where all sub-rules fail — health should be Degraded
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            // One healthy rule so the system initializes
            rule.For<AppConfig>().FromStaticJson("""{"Name": "healthy"}"""),
            // Optional aggregate — all sub-rules fail
            rule.For<AppConfig>().Aggregate(r => [
                r.FromFile("missing1.json"),
                r.FromFile("missing2.json")
            ])
        ])));

        Assert.Equal(HealthStatus.Degraded, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Aggregate_ThreeLayerMerge_CorrectPrecedence()
    {
        var manager = Track(ConfigManager.Create(c => c.UseConfiguration(rule =>
        [
            rule.For<AppConfig>().Aggregate(r => [
                r.FromStaticJson("""{"Name": "Layer1", "Version": 1, "Env": "base"}"""),
                r.FromStaticJson("""{"Version": 2, "Env": "env"}"""),
                r.FromStaticJson("""{"Env": "local"}""")
            ])
        ])));

        var config = manager.GetConfig<AppConfig>();

        Assert.NotNull(config);
        Assert.Equal("Layer1", config!.Name);  // only in layer 1
        Assert.Equal(2, config.Version);        // overridden in layer 2
        Assert.Equal("local", config.Env);      // overridden in layer 3
    }
}
