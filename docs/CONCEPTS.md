# Concepts Deep Dive

* **Rule**: Defines source + optional query + target configuration type.
* **Binding**: Maps concrete configuration types to interfaces for clean dependency injection.
* **Provider**: Pluggable source (file, environment, HTTP, adapter, static, custom).
* **Merge**: Ordered last-write-wins per flattened key (`Section:Key`) then rebound to your target type.
* **Arrays**: Replaced as whole values (no element-wise merge).
* **Recompute**: Any emitting provider triggers full ordered recompute → atomic snapshot swap.
* **Dynamic dependencies**: Rule factories (options/query) can read in-progress snapshots produced earlier.
* **Required vs Optional**: Required rule failure blocks that config type; optional failure skips the layer.
* **Auto-Registration**: Automatic DI registration of all rule types and bound interfaces with configurable default lifetime.
* **Service Control**: Fine-grained Add/Remove control over service lifetimes and keys.

---

## Binding System Concepts

### Interface-to-Concrete Mapping

Bindings create clean separation between configuration implementation and consumption:

```csharp
// Concrete configuration type (implementation)
public class DatabaseConfig : IDatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableLogging { get; set; } = true; // Not surfaced in interface
}

// Interface contract (consumption)  
public interface IDatabaseConfig
{
    string ConnectionString { get; }
    int TimeoutSeconds { get; }
}

// Binding maps concrete → interface
Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
```

### Multiple Interface Binding

One concrete type can implement multiple interfaces:

```csharp
public class FeatureConfig : IFeatureFlags, IReadOnlyFeatureFlags
{
    public bool EnableNewUI { get; set; }
    public bool EnableBetaFeatures { get; set; }
    public int MaxUsers { get; set; } = 100;
}

// Bind to both interfaces
Bind.Type<FeatureConfig>().To<IFeatureFlags>().To<IReadOnlyFeatureFlags>()
```

### Resolution Process

1. **Direct Access**: `GetConfig<DatabaseConfig>()` → direct snapshot lookup
2. **Interface Access**: `GetConfig<IDatabaseConfig>()` → binding registry lookup → snapshot lookup → cast to interface
3. **Validation**: Runtime verification that concrete type implements interface
4. **Null Handling**: Returns null if concrete type not found or doesn't implement interface

---

## DI Integration Concepts

### Auto-Registration Behavior

The DI package automatically registers services based on rules and bindings:

```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<DatabaseConfig>(),
    Rule.From.File("config.json").For<CacheConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
]);

// Automatically registers:
// - DatabaseConfig (Scoped)
// - CacheConfig (Scoped)  
// - IDatabaseConfig (Scoped) - mapped to DatabaseConfig
// - ConfigManager (Singleton)
```

### Service Lifetime Control

**Default Lifetime**: All services auto-registered as `Scoped` (configurable):
```csharp
options.DefaultRegistrationLifetime(ServiceLifetime.Singleton); // Change default
options.DefaultRegistrationLifetime(null); // Disable auto-registration
```

**Per-Service Control**:
```csharp
options.Register
    .Remove<DatabaseConfig>()                    // Prevent auto-registration
    .Add<DatabaseConfig>(ServiceLifetime.Singleton)  // Explicit registration
    .Add<IDatabaseConfig>(ServiceLifetime.Transient, "backup"); // Keyed service
```

### Keyed Services

Multiple registrations of the same type with different keys:

```csharp
options.Register
    .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "primary")
    .Add<IDatabaseConfig>(ServiceLifetime.Scoped, "secondary");

// Usage
var primary = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("primary");
var secondary = serviceProvider.GetRequiredKeyedService<IDatabaseConfig>("secondary");
```

### Service Resolution Hierarchy

1. **Keyed Services**: `GetRequiredKeyedService<T>("key")` → exact key match
2. **Default Services**: `GetRequiredService<T>()` → unkeyed registration  
3. **ConfigManager**: Always available for manual access: `configManager.GetConfig<T>()`

---

## Ordering & Dependencies

* Place dependency-producing rules before dependency-consuming rules.
* Rules may read any type's current snapshot during recompute. Avoid circular dependencies.

**Guidance for recompute-time reads:**

* `GetRequiredConfig<T>()` throws if T does not exist yet; use only if you guarantee T is produced earlier.
* `GetConfig<T>()` returns null if T does not exist.
* Seed dependency types explicitly with static rules to guarantee availability.

---

## Merge Semantics

* Last-write-wins per key using colon flattening.
* Arrays are replace-only.
* Nulls follow default JSON deserialization semantics.
