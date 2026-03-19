# Static & Observable Providers

These two providers serve different use cases but share a common trait: they don't read from external sources like files or HTTP endpoints.

## Static JSON

The static provider holds a fixed JSON value. It never changes.

```csharp
rule.For<AppSettings>().FromStaticJson("""{ "MaxRetries": 5, "Debug": true }""")
```

### Use Cases

**Hardcoded defaults:**

```csharp
rule => [
    rule.For<AppSettings>().FromStaticJson("""{ "MaxRetries": 3, "Debug": false }"""),
    rule.For<AppSettings>().FromFile("appsettings.json"),
]
```

The static rule provides a guaranteed baseline. The file overrides what it sets.

**Testing:**

```csharp
rule.For<AppSettings>().FromStaticJson("""{ "Feature": true }""")
```

Inject known values without needing files or environment variables.

**From an object:**

```csharp
rule.For<AppSettings>().FromStatic(a => new AppSettings
{
    MaxRetries = 10,
    Debug = true
})
```

The `FromStatic` overload serializes a C# object to JSON. The factory receives an `IConfigurationAccessor`, so it can derive values from earlier rules.

## Observable

The observable provider wraps any `IObservable<T>` as a configuration source. When the observable emits, it triggers a recompute.

```csharp
var configStream = new BehaviorSubject<FeatureConfig>(new FeatureConfig { Enabled = true });
rule.For<FeatureConfig>().FromObservable(configStream)
```

### From Objects

Pass an `IObservable<T>` where `T` is your configuration type. Each emitted value is serialized to JSON:

```csharp
IObservable<FeatureConfig> stream = /* your source */;
rule.For<FeatureConfig>().FromObservable(stream)
```

### From JSON Strings

Pass an `IObservable<string>` where each string is raw JSON:

```csharp
IObservable<string> jsonStream = /* WebSocket, message queue, etc. */;
rule.For<FeatureConfig>().FromObservable(jsonStream)
```

### From an Initial JSON String

Pass a JSON string to create a `BehaviorSubject` internally:

```csharp
rule.For<FeatureConfig>().FromObservable("""{ "Enabled": true }""")
```

This is a convenience initializer — equivalent to `FromStaticJson` for one-off values. It creates a `BehaviorSubject` internally, but you don't get a reference to it. If you need programmatic updates, create your own subject and pass it via `FromObservable(IObservable<T>)`:

```csharp
// gRPC stream → BehaviorSubject → provider
var configSubject = new BehaviorSubject<FeatureConfig>(defaultConfig);
grpcStream.Subscribe(update => configSubject.OnNext(update));

rule.For<FeatureConfig>().FromObservable(configSubject)
```

### Use Cases

- **WebSocket / gRPC streams** — wrap as `IObservable<T>` and pass to `FromObservable`
- **Message queue** — consume config updates from Kafka, RabbitMQ, etc.
- **In-process updates** — use `BehaviorSubject<T>` to push changes programmatically
- **Testing** — use `BehaviorSubject<T>` to simulate config changes over time

::: tip SSE Support Available
For Server-Sent Events, use the [HTTP provider](/guide/providers/http-polling) with `serverSentEvents: true` instead of building your own observable wrapper.
:::
