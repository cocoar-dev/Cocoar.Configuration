# Getting Started with Cocoar.Configuration

This guide walks through the most common usage patterns from basic setup to advanced scenarios.

## 1. Basic Usage

Load configuration from a static object or JSON string and read values:

```csharp
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

var manager = ConfigManager.Create(builder => builder
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromStaticJson(_ => """{"ApplicationName":"MyApp","EnableLogging":true}""")
    ]));

var settings = manager.GetConfig<AppSettings>();
Console.WriteLine(settings?.ApplicationName); // "MyApp"
```

## 2. File Configuration with Hot-Reload

Load configuration from a JSON file. Changes to the file trigger automatic recompute:

```csharp
var manager = ConfigManager.Create(builder => builder
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ]));

var settings = manager.GetConfig<AppSettings>();
```

The configuration is automatically reloaded when the file changes on disk (debounced at 300ms by default).

## 3. File Layering (Multiple Sources)

Combine multiple files with last-write-wins merge semantics:

```csharp
var manager = ConfigManager.Create(builder => builder
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json"),
        rules.For<AppSettings>().FromFile("appsettings.local.json").Optional()
    ]));
```

The second rule's values override the first (property-level merge). Missing files are ignored when marked `Optional()`.

## 4. Reactive Configuration

Subscribe to configuration changes for live updates:

```csharp
var reactive = manager.GetReactiveConfig<AppSettings>();

// Current value (always available, never null after init)
var current = reactive.CurrentValue;

// Subscribe to future changes (also immediately emits the current value)
using var subscription = reactive.Subscribe(settings =>
{
    Console.WriteLine($"Config updated: {settings.ApplicationName}");
});
```

`IReactiveConfig<T>` uses BehaviorSubject semantics: subscribing immediately emits the current value, then emits on every change.

## 5. Dependency Injection (ASP.NET Core)

Register Cocoar.Configuration with the DI container:

```csharp
// Program.cs
builder.AddCocoarConfiguration(config => config
    .UseConfiguration(
        rules => [
            rules.For<AppSettings>().FromFile("appsettings.json")
        ],
        setup => [
            setup.ConcreteType<AppSettings>()
        ]));

// Inject in services
public class MyService(AppSettings settings) { ... }
// Or reactive:
public class MyService(IReactiveConfig<AppSettings> reactiveSettings) { ... }
```

Scoped services receive a stable snapshot per request. Singletons receive `IReactiveConfig<T>` for live updates.

## 6. Async Initialization (`CreateAsync`)

For console apps or any context where blocking the calling thread during provider I/O is undesirable, use `CreateAsync` instead of `Create`:

```csharp
// Non-blocking startup — no threadpool thread is occupied during file/HTTP I/O
var manager = await ConfigManager.CreateAsync(builder => builder
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ]));

var settings = manager.GetConfig<AppSettings>();
```

Pass a `CancellationToken` to cancel startup (e.g., on application shutdown signal):

```csharp
var manager = await ConfigManager.CreateAsync(
    builder => builder.UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ]),
    cancellationToken: stoppingToken);
```

> **Note:** In ASP.NET Core the DI integration (`AddCocoarConfiguration`) handles initialization inside the host startup pipeline — `CreateAsync` is most useful in console apps, `IHostedService.StartAsync`, or any context that already has a natural `await` point.

## 7. Testing Overrides

Override configuration rules in tests without modifying application code:

```csharp
// Replace all rules with test-specific rules (skips file I/O)
using var _ = CocoarTestConfiguration.ReplaceConfiguration(
    rules => [
        rules.For<AppSettings>().FromStatic(_ => new AppSettings { ApplicationName = "TestApp" })
    ]);

// Application code that calls ConfigManager.Create() will use the test rules
var manager = ConfigManager.Create(appConfig);
var settings = manager.GetConfig<AppSettings>();
Assert.Equal("TestApp", settings?.ApplicationName);
```

For fixture-based patterns where the AsyncLocal context gap matters, use `TestConfigurationContext` and `CocoarTestConfiguration.Apply()`:

```csharp
public class MyFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rules => [
            rules.For<AppSettings>().FromStatic(_ => new AppSettings { ApplicationName = "TestApp" })
        ]);
}

public class MyTests(MyFixture fixture) : IClassFixture<MyFixture>, IDisposable
{
    private readonly TestConfigurationScope _scope = CocoarTestConfiguration.Apply(fixture.TestContext);
    public void Dispose() => _scope.Dispose();
}
```
