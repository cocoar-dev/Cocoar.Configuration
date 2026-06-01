using System.Reactive.Subjects;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.Configuration.Core.Tests.WhiteBox;

/// <summary>
/// Tests for IReactiveConfig with interface types.
/// Validates that interfaces exposed via ExposeAs work correctly with GetReactiveConfig.
/// </summary>
[Trait("Type", "Unit")]
[Trait("Feature", "InterfaceReactiveConfig")]
public class InterfaceReactiveConfigTests
{
    // Test interfaces and implementations
    public interface IAppSettings
    {
        string Name { get; }
        int Version { get; }
    }

    public record AppSettings(string Name, int Version) : IAppSettings;

    public interface IDatabaseSettings
    {
        string ConnectionString { get; }
    }

    public record DatabaseSettings(string ConnectionString) : IDatabaseSettings;

    public interface IFeatureFlags
    {
        bool EnableNewUI { get; }
    }

    public record FeatureFlags(bool EnableNewUI) : IFeatureFlags;

    private static (ConfigRule Rule, BehaviorSubject<T> Subject) CreateRule<T>(T initialValue)
    {
        var subject = new BehaviorSubject<T>(initialValue);
        var rule = ConfigRule.Create<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery>(
            _ => new(subject),
            _ => ObservableProviderQuery.Default,
            typeof(T),
            new() { Required = true });
        return (rule, subject);
    }

    private static ConfigManager CreateManager(
        ConfigRule[] rules,
        Func<SetupBuilder, SetupDefinition[]>? setup = null)
    {
        return ConfigManager.Create(c => c.UseConfiguration(rules, setup).UseLogger(NullLogger.Instance).UseDebounce(10));
    }

    [Fact]
    public void InterfaceReactiveConfig_CurrentValue_ReturnsCorrectValue()
    {
        var (rule, _) = CreateRule(new AppSettings("TestApp", 1));
        var mgr = CreateManager(
            [rule],
            setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

        var reactive = mgr.GetReactiveConfig<IAppSettings>();

        Assert.NotNull(reactive);
        Assert.NotNull(reactive.CurrentValue);
        Assert.Equal("TestApp", reactive.CurrentValue.Name);
        Assert.Equal(1, reactive.CurrentValue.Version);
    }

    [Fact]
    public async Task InterfaceReactiveConfig_Subscribe_ReceivesUpdates()
    {
        var (rule, subject) = CreateRule(new AppSettings("Initial", 1));
        var mgr = CreateManager(
            [rule],
            setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

        var reactive = mgr.GetReactiveConfig<IAppSettings>();
        var emissions = new List<IAppSettings>();
        using var sub = reactive.Subscribe(v => emissions.Add(v));

        // Update the configuration
        subject.OnNext(new AppSettings("Updated", 2));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Name == "Updated" && e.Version == 2),
            TimeSpan.FromSeconds(2),
            description: "interface reactive config emission after change");

        Assert.Contains(emissions, e => e.Name == "Updated" && e.Version == 2);
    }

    [Fact]
    public async Task InterfaceReactiveConfig_MultipleUpdates_AllReceived()
    {
        var (rule, subject) = CreateRule(new AppSettings("v1", 1));
        var mgr = CreateManager(
            [rule],
            setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

        var reactive = mgr.GetReactiveConfig<IAppSettings>();
        var emissions = new List<IAppSettings>();
        using var sub = reactive.Subscribe(v => emissions.Add(v));

        // Multiple rapid updates
        subject.OnNext(new AppSettings("v2", 2));
        await Task.Delay(50);
        subject.OnNext(new AppSettings("v3", 3));
        await Task.Delay(50);
        subject.OnNext(new AppSettings("v4", 4));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Version == 4),
            TimeSpan.FromSeconds(2),
            description: "final emission received");

        // Should have received updates (may be coalesced due to debouncing)
        Assert.True(emissions.Count >= 1);
        Assert.Equal(4, emissions.Last().Version);
    }

    [Fact]
    public void InterfaceReactiveConfig_NotExposed_ThrowsHelpfulError()
    {
        var (rule, _) = CreateRule(new AppSettings("Test", 1));
        // Note: NOT exposing AppSettings as IAppSettings
        var mgr = CreateManager([rule]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => mgr.GetReactiveConfig<IAppSettings>());

        Assert.Contains("ExposeAs", ex.Message);
        Assert.Contains("IAppSettings", ex.Message);
    }

    [Fact]
    public void InterfaceReactiveConfig_MultipleInterfaces_EachWorks()
    {
        var (appRule, _) = CreateRule(new AppSettings("App", 1));
        var (dbRule, _) = CreateRule(new DatabaseSettings("Server=localhost"));

        var mgr = CreateManager(
            [appRule, dbRule],
            setup => [
                setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
                setup.ConcreteType<DatabaseSettings>().ExposeAs<IDatabaseSettings>()
            ]);

        var appReactive = mgr.GetReactiveConfig<IAppSettings>();
        var dbReactive = mgr.GetReactiveConfig<IDatabaseSettings>();

        Assert.Equal("App", appReactive.CurrentValue.Name);
        Assert.Equal("Server=localhost", dbReactive.CurrentValue.ConnectionString);
    }

    [Fact]
    public async Task InterfaceReactiveConfig_WithConcreteReactive_BothWork()
    {
        var (rule, subject) = CreateRule(new AppSettings("Test", 1));
        var mgr = CreateManager(
            [rule],
            setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

        // Get both concrete and interface reactive configs
        var concreteReactive = mgr.GetReactiveConfig<AppSettings>();
        var interfaceReactive = mgr.GetReactiveConfig<IAppSettings>();

        var concreteEmissions = new List<AppSettings>();
        var interfaceEmissions = new List<IAppSettings>();

        using var concreteSub = concreteReactive.Subscribe(v => concreteEmissions.Add(v));
        using var interfaceSub = interfaceReactive.Subscribe(v => interfaceEmissions.Add(v));

        // Update configuration
        subject.OnNext(new AppSettings("Updated", 2));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => concreteEmissions.Any(e => e.Version == 2) &&
                  interfaceEmissions.Any(e => e.Version == 2),
            TimeSpan.FromSeconds(2),
            description: "both concrete and interface emissions received");

        Assert.Contains(concreteEmissions, e => e.Version == 2);
        Assert.Contains(interfaceEmissions, e => e.Version == 2);
    }

    [Fact]
    public async Task TupleWithInterface_Works()
    {
        var (appRule, appSubject) = CreateRule(new AppSettings("App", 1));
        var (dbRule, dbSubject) = CreateRule(new DatabaseSettings("Server=db"));

        var mgr = CreateManager(
            [appRule, dbRule],
            setup => [
                setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
                setup.ConcreteType<DatabaseSettings>().ExposeAs<IDatabaseSettings>()
            ]);

        // Get tuple with interface types
        var reactive = mgr.GetReactiveConfig<(IAppSettings, IDatabaseSettings)>();
        var emissions = new List<(IAppSettings, IDatabaseSettings)>();
        using var sub = reactive.Subscribe(v => emissions.Add(v));

        // Verify initial state
        var current = reactive.CurrentValue;
        Assert.Equal("App", current.Item1.Name);
        Assert.Equal("Server=db", current.Item2.ConnectionString);

        // Update one of the configs
        appSubject.OnNext(new AppSettings("UpdatedApp", 2));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Item1.Name == "UpdatedApp"),
            TimeSpan.FromSeconds(2),
            description: "tuple with interface emission after change");

        Assert.Contains(emissions, e => e.Item1.Name == "UpdatedApp" && e.Item2.ConnectionString == "Server=db");
    }

    [Fact]
    public async Task TupleWithMixedConcreteAndInterface_Works()
    {
        var (appRule, appSubject) = CreateRule(new AppSettings("App", 1));
        var (featureRule, featureSubject) = CreateRule(new FeatureFlags(false));

        var mgr = CreateManager(
            [appRule, featureRule],
            setup => [
                setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
                // Note: FeatureFlags is NOT exposed as interface
            ]);

        // Get tuple with mixed concrete and interface types
        var reactive = mgr.GetReactiveConfig<(IAppSettings, FeatureFlags)>();
        var emissions = new List<(IAppSettings, FeatureFlags)>();
        using var sub = reactive.Subscribe(v => emissions.Add(v));

        // Update both configs
        appSubject.OnNext(new AppSettings("NewApp", 2));
        featureSubject.OnNext(new FeatureFlags(true));

        await ActiveWaitHelpers.WaitUntilAsync(
            () => emissions.Any(e => e.Item1.Name == "NewApp" && e.Item2.EnableNewUI),
            TimeSpan.FromSeconds(2),
            description: "mixed tuple emission after change");

        Assert.Contains(emissions, e => e.Item1.Name == "NewApp" && e.Item2.EnableNewUI);
    }

    [Fact]
    public void InterfaceReactiveConfig_MultipleCalls_ShareUnderlyingData()
    {
        var (rule, _) = CreateRule(new AppSettings("Test", 1));
        var mgr = CreateManager(
            [rule],
            setup => [setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()]);

        var reactive1 = mgr.GetReactiveConfig<IAppSettings>();
        var reactive2 = mgr.GetReactiveConfig<IAppSettings>();

        // Interface adapters are created fresh each call, but they share the same underlying data
        // (they wrap the same cached concrete type reactive config)
        Assert.Equal(reactive1.CurrentValue.Name, reactive2.CurrentValue.Name);
        Assert.Equal(reactive1.CurrentValue.Version, reactive2.CurrentValue.Version);
    }
}
