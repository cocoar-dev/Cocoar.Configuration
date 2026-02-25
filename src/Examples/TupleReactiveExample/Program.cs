using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Examples.TupleReactiveExample;

// Example: Demonstrates tuple-based reactive configuration snapshots with arbitrary arity
// and interface exposure eligibility guard.
//
// Run:
//   dotnet run
// Then browse:
//   http://localhost:5088/snapshot
//   http://localhost:5088/raw
//   http://localhost:5088/update
//
// The /update endpoint simulates a change to one config type; the tuple stream emits
// at most once per recompute pass with aligned values.

// Cache array to avoid repeated allocations
var defaultFlags = new[] { "Alpha", "Beta" };

var builder = WebApplication.CreateBuilder(args);

// Subjects to simulate runtime changes (Observable provider emits these)
var appSubject = new System.Reactive.Subjects.BehaviorSubject<AppSettings>(new AppSettings { Message = "Hello World", Counter = 0 });
var flagsSubject = new System.Reactive.Subjects.BehaviorSubject<FeatureFlags>(new FeatureFlags { Flags = defaultFlags });

// Define configuration rules using observable providers for dynamic types and static JSON for logging
builder.Services.AddCocoarConfiguration(c => c.WithConfiguration(rule => [
    rule.For<AppSettings>().FromObservable(appSubject),
    rule.For<FeatureFlags>().FromObservable(flagsSubject),
    rule.For<LoggingConfig>().FromStaticJson("{ \"Level\": \"Info\" }")
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]));

var app = builder.Build();

// Grab single reactive configs (auto-registered)
var appReactive = app.Services.GetRequiredService<IReactiveConfig<AppSettings>>();
var flagsReactive = app.Services.GetRequiredService<IReactiveConfig<FeatureFlags>>();
var logReactive = app.Services.GetRequiredService<IReactiveConfig<LoggingConfig>>();

// Grab tuple reactive config (atomic, aligned)
var composite = app.Services.GetRequiredService<IReactiveConfig<(AppSettings App, FeatureFlags Flags, LoggingConfig Log)>>();

// Subscribe (fire-and-forget) for demonstration
_ = composite.Subscribe(t =>
{
    var (a, f, l) = t;
    Console.WriteLine($"Tuple emission -> Counter={a.Counter} Flags={string.Join(',', f.Flags)} Level={l.Level}");
});

app.MapGet("/snapshot", (IReactiveConfig<(AppSettings App, FeatureFlags Flags, LoggingConfig Log)> tuple) =>
{
    var currentValue = tuple.CurrentValue;
    return Results.Ok(new
    {
        currentValue.App.Message,
        currentValue.App.Counter,
        currentValue.Flags.Flags,
        currentValue.Log.Level
    });
});

app.MapGet("/raw", (AppSettings a, FeatureFlags f, LoggingConfig l) => new
{
    a.Message,
    a.Counter,
    f.Flags,
    l.Level
});

// Simulate an update by layering a later (higher precedence) dynamic rule.
// In real scenarios you'd have file / http / environment providers triggering changes.
app.MapPost("/update", () =>
{
    // Push new values through subjects; tuple reactive config will emit once with aligned snapshot.
    var current = appReactive.CurrentValue;
    appSubject.OnNext(new AppSettings { Message = current.Message, Counter = current.Counter + 1 });

    var currentFlags = flagsReactive.CurrentValue;
    if (!Enumerable.Contains(currentFlags.Flags, "Gamma"))
    {
        flagsSubject.OnNext(new FeatureFlags { Flags = currentFlags.Flags.Concat(["Gamma"]).ToArray() });
    }
    return Results.Accepted();
});

// Demonstrate guard (uncomment to see exception at runtime during resolution)
// var bad = app.Services.GetRequiredService<IReactiveConfig<(AppSettings, IUnexposed, LoggingConfig)>>();

app.Run();

namespace Examples.TupleReactiveExample
{ // Types
    public sealed class AppSettings : IAppSettings
    {
        public string Message { get; set; } = "";
        public int Counter { get; set; }
    }
    public interface IAppSettings
    {
        string Message { get; }
        int Counter { get; }
    }
    public sealed class FeatureFlags
    {
        public string[] Flags { get; set; } = Array.Empty<string>();
    }
    public sealed class LoggingConfig
    {
        public string Level { get; set; } = "Info";
    }
// public interface IUnexposed { } // Example of an unexposed interface that would fail eligibility
}
