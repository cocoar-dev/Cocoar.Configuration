---
description: Microsoft.Extensions.Logging with source-generated LoggerMessage, Cocoar.Configuration log categories, event IDs by Debug/Information/Warning level, filtering by prefix
---

# Logging & Diagnostics

Cocoar.Configuration uses `Microsoft.Extensions.Logging` with source-generated log messages (`[LoggerMessage]`) for structured, high-performance logging. This page covers how to configure log output and interpret the diagnostics the library emits.

## Log Categories

Each internal class creates its log messages under its own category (the fully-qualified class name). The main categories you will see are:

| Category | Area |
|---|---|
| `Cocoar.Configuration.Core.ConfigurationEngine` | Recompute lifecycle (start, finish, cancellation, errors) |
| `Cocoar.Configuration.Core.ConfigurationState` | Snapshot publication, deserialization failures |
| `Cocoar.Configuration.Core.ConfigurationAccessor` | Fallback deserialization during recompute phase |
| `Cocoar.Configuration.Rules.RuleManager` | Rule evaluation, provider failures, transform caching |
| `Cocoar.Configuration.Infrastructure.RecomputeCoalescer` | Debounce timer errors |
| `Cocoar.Configuration.Infrastructure.ChangeSubscriptionManager` | Change subscription errors |
| `Cocoar.Configuration.Infrastructure.ExposureRegistry` | Interface-to-concrete type mapping |
| `Cocoar.Configuration.Infrastructure.ProviderRegistry` | Provider creation, acquire/release, disposal |
| `Cocoar.Configuration.Reactive.ReactiveConfigManager` | Reactive config wrapper creation |
| `Cocoar.Configuration.Reactive.ReactiveConfigurationFactory` | Reactive priming, tuple element resolution |
| `Cocoar.Configuration.Reactive.ReactiveTupleConfig` | Tuple stream errors, tuple emission failures |

Because all categories start with `Cocoar.Configuration`, you can configure them with a single filter prefix.

## Log Levels

### Debug

Debug messages trace the internal mechanics of the configuration engine. These are useful when troubleshooting why a recompute fired or why a provider was recreated.

| Event ID | Source | Message |
|---|---|---|
| 2002 | `ConfigurationEngine` | Recompute started |
| 2003 | `ConfigurationEngine` | Recompute cancelled |
| 2004 | `ConfigurationEngine` | Recompute finished |
| 5003 | `RuleManager` | Query key hash failed for {QueryType}; falling back to JSON serialization |
| 5004 | `RuleManager` | Transform key computation failed; falling back to empty key |
| 3000 | `ConfigurationAccessor` | Fallback deserialization for {TypeName} during recompute phase |
| 3000 | `ExposureRegistry` | ConfigureSpec does not have a valid primary type capability, skipping |
| 3002 | `ExposureRegistry` | Exposed interface {InterfaceType} -> {ConcreteType} |
| 3004 | `ExposureRegistry` | Interface deserialization mapping: {InterfaceType} -> {ConcreteType} |
| 1000-1004 | `ProviderRegistry` | Provider creation, acquire, release, disposal (when diagnostics are enabled) |
| 6006 | `ReactiveConfigManager` | Created reactive config wrapper for type {Type} |

### Information

Information messages mark significant lifecycle events.

| Event ID | Source | Message |
|---|---|---|
| 2006 | `ConfigurationEngine` | Startup phase complete - switching to resilient mode |
| 4002 | `ConfigurationState` | Configuration snapshot published: version={Version}, types={TypeCount} |
| 3005 | `ExposureRegistry` | Built exposure registry with {ExposureCount} DI mappings and {DeserializationCount} deserialization mappings |
| 6000 | `ReactiveConfigManager` | Recreating dead observable for configuration type {Type} |

### Warning

Warnings indicate degraded conditions that the library recovers from automatically.

| Event ID | Source | Message |
|---|---|---|
| 4001 | `ConfigurationState` | Runtime deserialization failed for {FailureCount} types, keeping last good configuration |
| 5000 | `RuleManager` | Selection path '{SelectPath}' failed; skipping optional rule |
| 5002 | `RuleManager` | Optional rule failed and will be skipped: {Provider}->{Config} |
| 3001 | `ConfigurationAccessor` | Fallback deserialization failed for {TypeName}: {Message} |
| 3001 | `ExposureRegistry` | Interface {InterfaceType} was already exposed by {ExistingConcreteType}, now overridden by {NewConcreteType} |
| 3003 | `ExposureRegistry` | Interface {InterfaceType} deserialization was already mapped to {ExistingConcreteType}, now overridden by {NewConcreteType} |
| 6001 | `ReactiveConfigManager` | Failed to get initial config for type {Type}, using default value |
| 6100 | `ReactiveTupleConfig` | Tuple reactive config stream error ignored to keep alive for {TupleType} |
| 6101 | `ReactiveTupleConfig` | Failed to build CurrentValue for tuple {TupleType} |
| 6103 | `ReactiveTupleConfig` | Failed building tuple emission for {TupleType} |
| 6400 | `ReactiveConfigurationFactory` | Failed to locate GetReactiveConfig for type {Type} |
| 6401 | `ReactiveConfigurationFactory` | Failed to locate GetConfig for type {Type} |
| 6402 | `ReactiveConfigurationFactory` | Failed to prime reactive configuration for tuple element {Type} |
| 6403 | `ReactiveConfigurationFactory` | Type {Type} is not a class, skipping reactive priming |

### Error

Errors indicate failures that may require attention. Required rule failures prevent the application from starting (during startup) or roll back the recompute (at runtime).

| Event ID | Source | Message |
|---|---|---|
| 2000 | `ConfigurationEngine` | ConfigManager initialization failed |
| 2001 | `ConfigurationEngine` | Runtime recompute failed - preserving current configuration |
| 2005 | `ConfigurationEngine` | Recompute failed from change trigger |
| 4000 | `ConfigurationState` | Deserialization failed for {TypeName}: {Message} |
| 5001 | `RuleManager` | Required rule failed: {Provider}->{Config} |
| 4000 | `RecomputeCoalescer` | Recompute failed from initial debounce trigger |
| 4001 | `RecomputeCoalescer` | Recompute failed from trailing trigger |
| 4100 | `ChangeSubscriptionManager` | Recompute failed from change trigger |

## Configuring Log Levels

### Filter by Prefix

Because every log category starts with `Cocoar.Configuration`, you can control verbosity with a single filter:

```csharp
// In code
builder.Logging.AddFilter("Cocoar.Configuration", LogLevel.Debug);
```

```json
// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Cocoar.Configuration": "Debug"
    }
  }
}
```

### Fine-Grained Control

You can also filter individual subsystems:

```json
{
  "Logging": {
    "LogLevel": {
      "Cocoar.Configuration": "Warning",
      "Cocoar.Configuration.Core.ConfigurationEngine": "Debug",
      "Cocoar.Configuration.Rules.RuleManager": "Debug"
    }
  }
}
```

### Passing the Logger

When using `ConfigManager` directly (without DI), pass a logger via the builder:

```csharp
var loggerFactory = LoggerFactory.Create(b => b
    .AddConsole()
    .AddFilter("Cocoar.Configuration", LogLevel.Debug));

var manager = ConfigManager.Create(c => c
    .UseLogger(loggerFactory.CreateLogger<ConfigManager>())
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ]));
```

When using DI via `AddCocoarConfiguration`, the logger is resolved from the service container automatically.

## OpenTelemetry Integration

### Metrics

The library exposes metrics via `System.Diagnostics.Metrics` under the meter name **`Cocoar.Configuration`**. See the [Health Overview](/guide/health/overview#opentelemetry-metrics) for the full list of counters and histograms.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Cocoar.Configuration"));
```

### Distributed Tracing

The library emits traces via a `System.Diagnostics.ActivitySource` named **`Cocoar.Configuration`**. To collect these traces, add the source to your OpenTelemetry configuration:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Cocoar.Configuration"));
```

The following activities are emitted:

| Activity Name | Description | Tags |
|---|---|---|
| `cocoar.config.recompute` | Full recompute cycle | `rule_count`, `start_index`, `status` |
| `cocoar.config.rule` | Individual rule evaluation within a recompute | `rule_type`, `rule_index`, `required` |
| `cocoar.feature_flag.evaluate` | Feature flag evaluation | `flag.key`, `flag.kind` |
| `cocoar.entitlement.evaluate` | Entitlement evaluation | `flag.key`, `flag.kind` |

The `status` tag on `cocoar.config.recompute` is one of `success`, `cancelled`, or `failure`.

## Debounce Timing

The debounce interval controls how rapidly the engine reacts to configuration source changes. See [Debouncing](/guide/reactive/debouncing) for configuration details.

The `RecomputeCoalescer` logs errors at `Error` level if the debounce timer callback or trailing pass fails. The recompute lifecycle itself (start, cancel, finish) is logged at `Debug` level by `ConfigurationEngine`, so enabling Debug logging lets you observe when debounce windows close and recomputes fire.

```csharp
var manager = ConfigManager.Create(c => c
    .UseDebounce(500) // 500ms debounce
    .UseLogger(logger)
    .UseConfiguration(rules => [ /* ... */ ]));
```
