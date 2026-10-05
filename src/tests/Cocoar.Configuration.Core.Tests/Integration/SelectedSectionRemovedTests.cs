using System.Reactive.Subjects;
using Cocoar.Configuration.Core.Tests.TestUtilities;
using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.Core.Tests.Integration;

/// <summary>
/// A selected path that disappears from its source while the application runs must behave like the
/// same source at startup: the rule contributes nothing, instead of keeping the values it had before.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "ConfigManager")]
public class SelectedSectionRemovedTests
{
    public sealed class Logging { public string Level { get; set; } = "Default"; }
    public sealed class App { public string Name { get; set; } = ""; }
    public sealed class Host { public Logging Logging { get; set; } = new(); }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task OptionalRule_SectionRemovedAtRuntime_FallsBackToDefaults_AndNotifies()
    {
        using var source = new BehaviorSubject<string>("""{ "App": { "Name": "x" }, "Logging": { "Level": "Debug" } }""");
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
        [
            rules.For<Logging>().FromObservable(source).Select("Logging"),
            rules.For<App>().FromObservable(source).Select("App"),
        ]).UseDebounce(25));
        var logging = manager.GetReactiveConfig<Logging>();
        var levels = new List<string>();
        using var subscription = logging.Subscribe(value => { lock (levels) levels.Add(value.Level); });
        Assert.Equal("Debug", logging.CurrentValue.Level);

        source.OnNext("""{ "App": { "Name": "x" } }""");

        await ActiveWaitHelpers.WaitUntilAsync(() => logging.CurrentValue.Level == "Default", description: "defaults after the section was removed");
        lock (levels) Assert.Equal("Default", levels[^1]);
        Assert.Equal(HealthStatus.Degraded, manager.HealthStatus);

        // The rule recovers as soon as the section is back.
        source.OnNext("""{ "App": { "Name": "x" }, "Logging": { "Level": "Trace" } }""");

        await ActiveWaitHelpers.WaitUntilAsync(() => logging.CurrentValue.Level == "Trace", description: "values after the section returned");
        Assert.Equal(HealthStatus.Healthy, manager.HealthStatus);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task OptionalRule_SectionRemovedAtRuntime_WithMountAt_FallsBackToDefaults()
    {
        using var source = new BehaviorSubject<string>("""{ "Logging": { "Level": "Debug" } }""");
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
            [rules.For<Host>().FromObservable(source).Select("Logging").MountAt("Logging")]).UseDebounce(25));
        Assert.Equal("Debug", manager.GetConfig<Host>()!.Logging.Level);

        source.OnNext("{ }");

        await ActiveWaitHelpers.WaitUntilAsync(() => manager.GetConfig<Host>()!.Logging.Level == "Default", description: "defaults after the section was removed");
    }

    [Fact]
    [Trait("Type", "Unit")]
    public async Task RequiredRule_SectionRemovedAtRuntime_KeepsTheLastGoodSnapshot_AndReportsIt()
    {
        using var source = new BehaviorSubject<string>("""{ "Logging": { "Level": "Debug" } }""");
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
            [rules.For<Logging>().FromObservable(source).Select("Logging").Required()]).UseDebounce(25));

        source.OnNext("{ }");

        await ActiveWaitHelpers.WaitUntilAsync(() => manager.HealthStatus != HealthStatus.Healthy, description: "the failed required rule to be reported");
        Assert.Equal("Debug", manager.GetConfig<Logging>()!.Level);
    }
}
