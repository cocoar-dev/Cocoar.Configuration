using Cocoar.Configuration.Core.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Core.Tests.Core;

public class ConfigManagerBuilderTests
{
    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithRulesOnly_Works()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 42}""")
            ]));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
        Assert.Equal(42, config!.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithRulesAndSetup_Works()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(
                rules => [rules.For<TestConfig>().FromStaticJson("""{"Value": 7}""")],
                setup => [setup.ConcreteType<TestConfig>()]));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
        Assert.Equal(7, config!.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithPrebuiltRules_Works()
    {
        var rule = TestRules.StaticJson<TestConfig>("""{"Enabled": true}""");

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(new[] { rule }));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
        Assert.True(config!.Enabled);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithEmptyBuilder_Works()
    {
        using var manager = ConfigManager.Create(c => { });

        // No rules = no configs, but manager should be valid
        var result = manager.TryGetConfig<TestConfig>(out _);
        Assert.False(result);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithLogger_PassesLogger()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 1}""")
            ])
            .UseLogger(NullLogger.Instance));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_WithDebounce_PassesDebounce()
    {
        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 1}""")
            ])
            .UseDebounce(50));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void AfterBuild_ExecutesAfterInitialization()
    {
        var afterBuildCalled = false;
        ConfigManager? capturedManager = null;

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 99}""")
            ])
            .AfterBuild(m =>
            {
                afterBuildCalled = true;
                capturedManager = m;
                // Manager should be initialized — we can access config
                var config = m.GetConfig<TestConfig>();
                Assert.NotNull(config);
                Assert.Equal(99, config!.Value);
            }));

        Assert.True(afterBuildCalled);
        Assert.Same(manager, capturedManager);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void AfterBuild_MultipleActions_ExecuteInOrder()
    {
        var order = new List<int>();

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 1}""")
            ])
            .AfterBuild(_ => order.Add(1))
            .AfterBuild(_ => order.Add(2))
            .AfterBuild(_ => order.Add(3)));

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void Create_NullConfigure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigManager.Create(null!));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public void AfterBuild_NullAction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            ConfigManager.Create(c => c.AfterBuild(null!));
        });
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public async Task CreateAsync_ReturnsInitializedManager()
    {
        await using var manager = await ConfigManager.CreateAsync(c => c
            .UseConfiguration(rules => [
                rules.For<TestConfig>().FromStaticJson("""{"Value": 42}""")
            ]));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
        Assert.Equal(42, config!.Value);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public async Task CreateAsync_WithCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConfigManager.CreateAsync(
                c => c.UseConfiguration(rules => [
                    rules.For<TestConfig>().FromStaticJson("""{"Value": 1}""")
                ]),
                cts.Token));
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public async Task CreateAsync_WithStaticJson_ProducesCorrectConfig()
    {
        await using var manager = await ConfigManager.CreateAsync(c => c
            .UseConfiguration(
                rules => [
                    rules.For<TestConfig>().FromStaticJson("""{"Value": 7, "Enabled": true}""")
                ],
                setup => [setup.ConcreteType<TestConfig>()]));

        var config = manager.GetConfig<TestConfig>();
        Assert.NotNull(config);
        Assert.Equal(7, config!.Value);
        Assert.True(config.Enabled);
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Component", "ConfigManagerBuilder")]
    public async Task CreateAsync_NullConfigure_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ConfigManager.CreateAsync(null!));
    }

    public sealed class TestConfig
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
    }
}
