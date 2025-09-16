# Advanced Features

## Complete DI Integration Package

The `Cocoar.Configuration.DI` package provides zero-configuration dependency injection with progressive enhancement capabilities.

### Zero-Config Auto-Registration

Just add rules and everything works:

```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<DatabaseConfig>(),
    Rule.From.Environment("CACHE_").For<CacheConfig>()
]);

// Automatically registers:
// - DatabaseConfig as Scoped
// - CacheConfig as Scoped  
// - ConfigManager as Singleton
```

### Interface Binding Integration

Add clean interface contracts without additional setup:

```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<DatabaseConfig>(),
    Rule.From.File("config.json").For<PaymentConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>(),
    Bind.Type<PaymentConfig>().To<IPaymentConfig>().To<IReadOnlyPaymentConfig>()
]);

// Auto-registers both concrete types AND all bound interfaces
```

👉 Example: [`src/Examples/DIExample/Program.cs`](../src/Examples/DIExample/Program.cs)

---

## Advanced Service Lifetime Control

### Configurable Default Lifetime

Change the default registration lifetime for all services:

```csharp
services.AddCocoarConfiguration([rules], [bindings], options => {
    options.DefaultRegistrationLifetime(ServiceLifetime.Singleton); // All services as Singleton
    // OR
    options.DefaultRegistrationLifetime(null); // Disable auto-registration entirely
});
```

### Fine-Grained Per-Service Control

Use Remove/Add for surgical control over individual services:

```csharp
services.AddCocoarConfiguration([rules], [bindings], options => {
    options.Register
        .Remove<DatabaseConfig>()                                    // Prevent auto-registration
        .Add<DatabaseConfig>(ServiceLifetime.Singleton)              // Explicit singleton
        .Add<IPaymentConfig>(ServiceLifetime.Transient, "backup")    // Keyed transient service
        .Add<IPaymentConfig>(ServiceLifetime.Scoped, "primary");     // Keyed scoped service
});
```

### Keyed Services Pattern

Register multiple configurations of the same type for different purposes:

```csharp
// Registration
options.Register
    .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "read-only")
    .Add<IDatabaseConfig>(ServiceLifetime.Scoped, "read-write")
    .Add<IPaymentConfig>(ServiceLifetime.Singleton, "stripe")
    .Add<IPaymentConfig>(ServiceLifetime.Singleton, "paypal");

// Usage in services
public class PaymentService
{
    private readonly IPaymentConfig _stripe;
    private readonly IPaymentConfig _paypal;
    
    public PaymentService(
        [FromKeyedServices("stripe")] IPaymentConfig stripe,
        [FromKeyedServices("paypal")] IPaymentConfig paypal)
    {
        _stripe = stripe;
        _paypal = paypal;
    }
}
```

👉 Example: [`src/Examples/ServiceLifetimes/Program.cs`](../src/Examples/ServiceLifetimes/Program.cs)

---

## Pure Core Library Usage

Use Cocoar.Configuration without any DI framework via the ConfigManager directly:

```csharp
var configManager = new ConfigManager([
    Rule.From.File("config.json").For<AppConfig>(),
    Rule.From.Static(new { Environment = "Production" }).For<AppConfig>()
], [
    Bind.Type<AppConfig>().To<IAppConfig>()
]).Initialize();

// Direct access
var appConfig = configManager.GetConfig<AppConfig>();
var appInterface = configManager.GetConfig<IAppConfig>(); // Via binding resolution

Console.WriteLine($"Environment: {appConfig.Environment}");
```

👉 Example: [`src/Examples/SimplifiedCoreExample/Program.cs`](../src/Examples/SimplifiedCoreExample/Program.cs)

---

## Interface Binding Without DI

Use the binding system for clean interfaces without a DI container:

```csharp
var manager = new ConfigManager([rules], [
    Bind.Type<PaymentConfig>().To<IPaymentConfig>(),
    Bind.Type<FeatureConfig>().To<IFeatureFlags>().To<IReadOnlyFeatureFlags>()
]);

// Clean interface access
var paymentInterface = manager.GetConfig<IPaymentConfig>();
var featureFlags = manager.GetConfig<IFeatureFlags>();
var readOnlyFlags = manager.GetConfig<IReadOnlyFeatureFlags>(); // Same concrete instance

// Concrete access still available
var paymentConcrete = manager.GetConfig<PaymentConfig>();
Console.WriteLine($"Internal Notes: {paymentConcrete.InternalNotes}"); // Not in interface
```

👉 Example: [`src/Examples/BindingExample/Program.cs`](../src/Examples/BindingExample/Program.cs)

---

## Pre-Built ConfigManager Integration

Build ConfigManager manually and integrate with DI for advanced scenarios:

```csharp
// Build ConfigManager with full control
var configManager = new ConfigManager([
    Rule.From.File("config.json").For<DatabaseConfig>(),
    Rule.From.Environment("CACHE_").For<CacheConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
]).Initialize();

// Inspect before registering
Console.WriteLine($"Rules: {configManager.Rules.Count}");
Console.WriteLine($"Bindings: {configManager.Bindings.Count}");

// Use in DI with full auto-registration
services.AddCocoarConfiguration(configManager, options => {
    options.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
    options.Register.Add<IDatabaseConfig>(ServiceLifetime.Scoped, "backup");
});
```

---

## Generic Provider API

Use `Rule.From.Provider<TProvider, TOptions, TQuery>()` for full control over provider composition.

👉 Example: [`src/Examples/GenericProviderAPI/Program.cs`](../src/Examples/GenericProviderAPI/Program.cs)

---

## Microsoft Configuration Adapter

Plug any Microsoft `IConfigurationSource` (JSON, XML, Key Vault, User Secrets, etc.).

👉 Example: [`src/Examples/MicrosoftAdapterExample/Program.cs`](../src/Examples/MicrosoftAdapterExample/Program.cs)

---

## HTTP Polling Provider

Fetch config from HTTP endpoints with polling & change detection.

👉 Example: [`src/Examples/HttpPollingExample/Program.cs`](../src/Examples/HttpPollingExample/Program.cs)

---

## Best Practices

### Service Lifetime Guidelines

- **Singleton**: Use for expensive-to-create configs or truly global settings
- **Scoped**: Default choice for web applications (same config per request)  
- **Transient**: Use when config might be modified by consumers or for isolation

### Interface Design Patterns

- **Full Interface**: Include all properties for complete access
- **Read-Only Interface**: Hide setters for immutable consumption
- **Specialized Interface**: Include only properties relevant to a specific use case

```csharp
public class DatabaseConfig : IDatabaseConfig, IReadOnlyDatabaseConfig, IConnectionConfig
{
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableLogging { get; set; } = true;
    public string InternalId { get; set; } = ""; // Not surfaced by any interface
}

public interface IDatabaseConfig // Full access
{
    string ConnectionString { get; set; }
    int TimeoutSeconds { get; set; }
    bool EnableLogging { get; set; }
}

public interface IReadOnlyDatabaseConfig // Immutable consumption
{
    string ConnectionString { get; }
    int TimeoutSeconds { get; }
    bool EnableLogging { get; }
}

public interface IConnectionConfig // Specialized view
{
    string ConnectionString { get; }
}
```

### Keyed Service Organization

Use meaningful service keys that reflect purpose:
- Environment-based: `"production"`, `"staging"`, `"development"`
- Feature-based: `"primary"`, `"backup"`, `"fallback"`
- Provider-based: `"stripe"`, `"paypal"`, `"manual"`
- Component-based: `"cache"`, `"database"`, `"logging"`
