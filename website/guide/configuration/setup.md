# Setup & Type Exposure

Setup controls how configuration types are registered and exposed. It's the optional second parameter to `UseConfiguration()`.

## Auto-Registration

If you don't provide any setup, all types from your rules are automatically registered:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rule => [
        rule.For<AppSettings>().FromFile("appsettings.json"),
        rule.For<DatabaseConfig>().FromFile("database.json"),
    ]));
```

Both `AppSettings` and `DatabaseConfig` are registered as **Scoped** in DI. You can inject them directly — no `IOptions<T>` wrapper:

```csharp
public class MyService(AppSettings settings, DatabaseConfig db)
{
    // Just use them
}
```

For many applications, this is all you need. Setup is only required when you want to customize how types are exposed.

## The Setup Lambda

Setup is a second lambda passed to `UseConfiguration()`:

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(
        rule => [
            rule.For<AppSettings>().FromFile("appsettings.json"),
            rule.For<DatabaseConfig>().FromFile("database.json"),
        ],
        setup => [
            setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
            setup.Interface<IDatabase>().DeserializeTo<PostgresDatabase>(),
        ]));
```

The setup callback receives a `SetupBuilder` and returns an array of `SetupDefinition[]`. Two methods are available:

| Method | Purpose |
|---|---|
| `setup.ConcreteType<T>()` | Configure how a concrete type is registered |
| `setup.Interface<T>()` | Configure deserialization for interface-typed properties |

## ConcreteType — Interface Exposure

Use `.ConcreteType<T>().ExposeAs<TInterface>()` when consumers should depend on an abstraction:

```csharp
public interface IAppSettings
{
    string AppName { get; }
    int MaxRetries { get; }
}

public class AppSettings : IAppSettings
{
    public string AppName { get; set; } = "MyApp";
    public int MaxRetries { get; set; } = 3;
}
```

```csharp
setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]
```

Now you can inject either the concrete type or the interface:

```csharp
// Both work
public class ServiceA(AppSettings settings) { }
public class ServiceB(IAppSettings settings) { }
```

You can expose a type through multiple interfaces by chaining:

```csharp
setup.ConcreteType<AppSettings>()
    .ExposeAs<IAppSettings>()
    .ExposeAs<IRetrySettings>()
```

::: warning Type Safety
`ExposeAs<T>()` validates at call time:
- `T` must be an interface — classes are rejected
- The concrete type must implement `T` — otherwise you get an `InvalidOperationException`
:::

## Interface — Deserialization Mapping

Use `.Interface<TInterface>().DeserializeTo<TConcrete>()` when your configuration classes have interface-typed properties:

```csharp
public class AppSettings
{
    public string AppName { get; set; } = "MyApp";
    public IDatabase Database { get; set; }   // Interface property
    public ICache Cache { get; set; }          // Interface property
}
```

JSON deserializers can't instantiate interfaces. The setup tells the system which concrete type to create:

```csharp
setup => [
    setup.Interface<IDatabase>().DeserializeTo<PostgresDatabase>(),
    setup.Interface<ICache>().DeserializeTo<RedisCache>(),
]
```

When the JSON is deserialized, any `IDatabase` property gets a `PostgresDatabase` instance, and any `ICache` property gets a `RedisCache` instance.

::: info When to use which
- **`ConcreteType<T>().ExposeAs<I>()`** — Controls DI registration. "Register `AppSettings` and also let people inject `IAppSettings`."
- **`Interface<I>().DeserializeTo<T>()`** — Controls deserialization. "When JSON has an `IDatabase` property, create a `PostgresDatabase`."

They solve different problems. Use both when needed.
:::

## DI Lifetime Modifiers <Badge type="info" text="ADV" />

The `Cocoar.Configuration.DI` package adds lifetime methods to `ConcreteType<T>()`:

```csharp
setup => [
    setup.ConcreteType<CacheSettings>().AsSingleton(),
    setup.ConcreteType<RequestSettings>().AsTransient(),
    setup.ConcreteType<AppSettings>().AsScoped(),      // Default, rarely needed explicitly
]
```

| Method | Lifetime | Use When |
|---|---|---|
| `.AsScoped()` | Scoped | Default — stable snapshot per request |
| `.AsSingleton()` | Singleton | Config rarely changes, shared across requests |
| `.AsTransient()` | Transient | New instance per injection |

### Keyed Services <Badge type="info" text="ADV" />

All lifetime methods accept an optional key for [keyed services](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#keyed-services):

```csharp
setup => [
    setup.ConcreteType<DatabaseConfig>().AsSingleton("primary"),
    setup.ConcreteType<DatabaseConfig>().AsSingleton("replica"),
]
```

### Disabling Auto-Registration <Badge type="info" text="ADV" />

If you want a type to load but not be registered in DI:

```csharp
setup => [
    setup.ConcreteType<InternalSettings>().DisableAutoRegistration()
]
```

The type still loads from its rules and is accessible via `ConfigManager.GetConfig<InternalSettings>()`, but it won't appear in the DI container. `IReactiveConfig<InternalSettings>` is still registered.

::: tip
For full details on DI lifetimes, keyed services, and registration behavior, see [DI Lifetimes](/guide/di/lifetimes).
:::

## What Gets Registered

For every configuration type, the DI system registers:

| Registration | Lifetime | Description |
|---|---|---|
| `T` | Scoped (default) | The config type itself |
| `IReactiveConfig<T>` | Singleton (always) | Live-updating observable stream |
| Any `ExposeAs<I>()` interfaces | Same as `T` | Interface aliases |

`IReactiveConfig<T>` is always Singleton regardless of the concrete type's lifetime — it represents a continuous stream of updates, not a point-in-time snapshot.

## Without DI <Badge type="info" text="ADV" />

In console apps or tests using `ConfigManager.Create()`, setup still works for deserialization mapping:

```csharp
using var manager = ConfigManager.Create(c => c
    .UseConfiguration(
        rule => [
            rule.For<AppSettings>().FromFile("appsettings.json"),
        ],
        setup => [
            setup.Interface<IDatabase>().DeserializeTo<PostgresDatabase>(),
        ]));

var settings = manager.GetConfig<AppSettings>();
// settings.Database is a PostgresDatabase instance
```

The `ExposeAs` and lifetime modifiers are DI-only concepts — they have no effect without a DI container.
