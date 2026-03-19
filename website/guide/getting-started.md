# Getting Started

## Install

Pick the package that matches your scenario — each one includes everything above it:

```shell
dotnet add package Cocoar.Configuration              # Core library (console apps, no DI)
dotnet add package Cocoar.Configuration.DI           # ↑ + Microsoft.Extensions.DI integration
dotnet add package Cocoar.Configuration.AspNetCore    # ↑ + health endpoints, feature flag endpoints
```

You only need **one** of these — install the highest one you need.

Optional packages for additional providers:

```shell
dotnet add package Cocoar.Configuration.Http               # Remote config via HTTP
dotnet add package Cocoar.Configuration.MicrosoftAdapter   # Bridge existing IConfiguration
```

## Your First Configuration

### 1. Define a configuration class

```csharp
public class AppSettings
{
    public string AppName { get; set; } = "MyApp";
    public int MaxRetries { get; set; } = 3;
    public bool EnableLogging { get; set; } = true;
}
```

No base class, no attributes, no interfaces. Just a plain C# class.

### 2. Create a JSON config file

**appsettings.json:**
```json
{
  "AppName": "My Application",
  "MaxRetries": 5,
  "EnableLogging": true
}
```

### 3. Wire it up

::: code-group

```csharp [ASP.NET Core]
var builder = WebApplication.CreateBuilder(args);

builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json")
    ]));

var app = builder.Build();

// Inject directly — no IOptions<T> wrapper
app.MapGet("/settings", (AppSettings settings) => new
{
    settings.AppName,
    settings.MaxRetries
});

app.Run();
```

```csharp [Console App]
using var manager = ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json")
    ]));

var settings = manager.GetConfig<AppSettings>();
Console.WriteLine($"App: {settings.AppName}, Retries: {settings.MaxRetries}");
```

:::

That's it. `AppSettings` is loaded and ready to inject.

## Layering Multiple Sources

The real power comes from layering. Rules execute in order — last write wins:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),          // Base
        rule.For<AppSettings>().FromFile("appsettings.Production.json"), // Override per environment
        rule.For<AppSettings>().FromEnvironment("APP_"),               // Override from env vars
    ]));
```

With this setup:
- `appsettings.json` provides defaults
- `appsettings.Production.json` overrides what it sets
- Environment variables like `APP_MaxRetries=10` override everything

Properties merge at the JSON level. A later rule only overrides the properties it defines — everything else keeps the value from earlier rules.

## Live Reloading

When a file changes on disk, configuration updates automatically. Subscribe to changes:

```csharp
public class NotificationService(IReactiveConfig<AppSettings> config)
{
    public void Start()
    {
        // Called immediately with current value, then on every change
        config.Subscribe(settings =>
        {
            Console.WriteLine($"Config updated: MaxRetries={settings.MaxRetries}");
        });
    }
}
```

`IReactiveConfig<T>` is automatically registered in DI for every configuration type.

## What Happens Under the Hood

When you call `ConfigManager.Create()` or `AddCocoarConfiguration()`:

1. **Rules are evaluated** in order — each provider fetches its data (reads file, scans env vars, etc.)
2. **JSON is merged** — later rules overlay earlier ones, property by property
3. **Types are deserialized** — the merged JSON becomes your strongly-typed C# object
4. **Change detection starts** — file watchers, polling timers, etc. monitor for changes
5. **Updates are atomic** — when a source changes, the full recompute runs and all subscribers get the new snapshot at once

## Next Steps

- [Rules & Layering](/guide/configuration/rules) — Deep dive into the rule system
- [Providers](/guide/providers/overview) — All available configuration sources
- [Reactive Updates](/guide/reactive/basics) — Subscribe to live config changes
- [DI Integration](/guide/di/setup) — Lifetimes, type exposure, ASP.NET Core setup
