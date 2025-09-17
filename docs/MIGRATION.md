# Migration Guide

This guide helps you upgrade from previous versions of Cocoar.Configuration to the latest version with the new Binding system and complete DI integration package.

---

## Breaking Changes Summary

### ⚠️ BREAKING: Core Library DI Features Removed

DI-related features have been moved from the core library to a dedicated DI integration package:

| **Old (Removed from Core)** | **New Location** |
|------------------------------|------------------|
| `.As<Interface>()` fluent methods | `Bind.Type<T>().To<Interface>()` + DI package |
| `ServiceLifetime` in rule builders | DI package `ServiceRegistrationOptions` |
| Built-in DI registration | `Cocoar.Configuration.DI` package |
| `.For<T>(ServiceLifetime)` overloads | DI package lifetime management |

### Additional Changes (Non-Breaking but Notable)

| Area | Change |
|------|--------|
| Reactive Config | Former separate resilient reactive implementation merged into a single `IReactiveConfig<T>` pipeline (hash-gated, error-resilient). No action required for consumers—existing reactive usage continues to work. |

### ⚠️ BREAKING: Manual DI Registration Required

- **Old**: DI features were built into the core library
- **New**: Dedicated `Cocoar.Configuration.DI` package with auto-registration

### Quick Reference: Old → New

| Scenario | Old Fluent API | New API (Binding + Options) |
|----------|----------------|-----------------------------|
| Single interface map | `.For<Foo>().As<IFoo>()` | Rule: `.For<Foo>()` + Binding: `Bind.Type<Foo>().To<IFoo>()` |
| Multiple interface maps | `.For<Foo>().As<IFoo>().As<IBar>()` | `Bind.Type<Foo>().To<IFoo>().To<IBar>()` |
| Lifetime inline | `.As<IFoo>(ServiceLifetime.Singleton)` | `options.Register.Add<IFoo>(ServiceLifetime.Singleton)` |
| Keyed registration | `.As<IFoo>(ServiceLifetime.Scoped, "blue")` | `options.Register.Add<IFoo>(ServiceLifetime.Scoped, "blue")` |
| Default lifetime override | Per-call variations | `options.DefaultRegistrationLifetime(ServiceLifetime.Singleton)` |
| Disable auto-registration | Manual DI only | `options.DefaultRegistrationLifetime(null)` + explicit `options.Register.Add<T>()` |
| Concrete only DI | Manual factory delegate | Just rules: `services.AddCocoarConfiguration([rules]);` |
| Manual remove & replace | Not supported inline | `options.Register.Remove<IFoo>().Add<IFoo>(ServiceLifetime.Transient)` |

Use this table for fast mechanical migration before refining structure.

---

## Migration Steps

### Step 1: Add DI Integration Package

**Add the DI package** to your project:

```xml
<PackageReference Include="Cocoar.Configuration.DI" Version="1.0.0" />
```

**Add using statement** where needed:
```csharp
using Cocoar.Configuration.DI; // For DI integration
```

### Step 2: Remove Old Fluent DI API

**Before (Old Fluent API):**
```csharp
services.AddCocoarConfiguration(
    Rule.From.File("config.json").Select("Database")
        .For<DatabaseConfig>()
        .As<IDatabaseConfig>(),                    // ❌ Removed
    Rule.From.File("config.json").Select("Payment")
        .For<PaymentConfig>()
        .As<IPaymentConfig>(ServiceLifetime.Scoped, "primary")  // ❌ Removed
);
```

**After (New Binding + DI API):**
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").Select("Database")
        .For<DatabaseConfig>(),
    Rule.From.File("config.json").Select("Payment")
        .For<PaymentConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>(),
    Bind.Type<PaymentConfig>().To<IPaymentConfig>()
], options => {
    options.Register.Add<IPaymentConfig>(ServiceLifetime.Scoped, "primary");
});
```

### Step 3: Replace Manual DI Registration

**Before (Manual DI Registration):**
```csharp
// Manual registration patterns
services.AddSingleton<ConfigManager>();
services.AddTransient<PaymentConfig>(provider => 
    provider.GetService<ConfigManager>().GetConfig<PaymentConfig>());
services.AddScoped<IPaymentConfig>(provider => 
    provider.GetService<ConfigManager>().GetConfig<IPaymentConfig>());
```

**After (Auto-Registration):**
```csharp
// Zero-config auto-registration
services.AddCocoarConfiguration([rules], [bindings]);

// Everything automatically registered:
// - PaymentConfig (Scoped)
// - IPaymentConfig (Scoped) 
// - ConfigManager (Singleton)
```

---

## Detailed Migration Examples

### Example 1: Basic Interface Registration

**Before (Old Core API):**
```csharp
services.AddCocoarConfiguration(
    Rule.From.File("config.json").For<DatabaseConfig>().As<IDatabaseConfig>(),
    Rule.From.Environment("CACHE_").For<CacheConfig>()
);

// Manual service registration
services.AddScoped<IDatabaseConfig>(provider => 
    provider.GetService<ConfigManager>().GetConfig<IDatabaseConfig>());
```

**After (New DI Package):**
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<DatabaseConfig>(),
    Rule.From.Environment("CACHE_").For<CacheConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
]);

// Auto-registered services:
// ✅ DatabaseConfig (Scoped)
// ✅ CacheConfig (Scoped)
// ✅ IDatabaseConfig (Scoped)
// ✅ ConfigManager (Singleton)
```

### Example 2: Service Lifetime Control

**Before (Fluent Lifetime API):**
```csharp
services.AddCocoarConfiguration(
    Rule.From.File("config.json")
        .For<PaymentConfig>()
        .As<IPaymentConfig>(ServiceLifetime.Singleton),
    Rule.From.File("config.json")
        .For<CacheConfig>()
        .As<ICacheConfig>(ServiceLifetime.Transient)
);
```

**After (Options-Based Lifetime Control):**
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<PaymentConfig>(),
    Rule.From.File("config.json").For<CacheConfig>()
], [
    Bind.Type<PaymentConfig>().To<IPaymentConfig>(),
    Bind.Type<CacheConfig>().To<ICacheConfig>()
], options => {
    options.DefaultRegistrationLifetime(ServiceLifetime.Scoped); // Default for all
    options.Register
        .Remove<IPaymentConfig>()  // Prevent auto-registration
        .Add<IPaymentConfig>(ServiceLifetime.Singleton)
        .Remove<ICacheConfig>()
        .Add<ICacheConfig>(ServiceLifetime.Transient);
});
```

### Example 3: Keyed Services

**Before (Fluent Keyed Services):**
```csharp
services.AddCocoarConfiguration(
    Rule.From.File("primary.json").For<DatabaseConfig>()
        .As<IDatabaseConfig>(ServiceLifetime.Singleton, "primary"),
    Rule.From.File("backup.json").For<DatabaseConfig>()
        .As<IDatabaseConfig>(ServiceLifetime.Scoped, "backup")
);
```

**After (Options-Based Keyed Services):**
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("primary.json").For<DatabaseConfig>(),
    Rule.From.File("backup.json").For<DatabaseConfig>()
], [
    Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
], options => {
    options.DefaultRegistrationLifetime(null); // Disable auto-registration
    options.Register
        .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "primary")
        .Add<IDatabaseConfig>(ServiceLifetime.Scoped, "backup");
});
```

### Example 4: Multiple Interface Binding

**Before (Chained Fluent API):**
```csharp
services.AddCocoarConfiguration(
    Rule.From.File("config.json").For<FeatureConfig>()
        .As<IFeatureFlags>()
        .As<IReadOnlyFeatureFlags>()
);
```

**After (Multiple Binding API):**
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<FeatureConfig>()
], [
    Bind.Type<FeatureConfig>().To<IFeatureFlags>().To<IReadOnlyFeatureFlags>()
]);

// Both interfaces auto-registered and resolve to same FeatureConfig instance
```

### Example 5: Pure Core Usage (No DI)

**Before (Core + Manual Interface Access):**
```csharp
var manager = new ConfigManager([
    Rule.From.File("config.json").For<AppConfig>()
]);

// Manual interface resolution required
var appConfig = manager.GetConfig<AppConfig>();
// No direct interface access available
```

**After (Core + Binding System):**
```csharp
var manager = new ConfigManager([
    Rule.From.File("config.json").For<AppConfig>()
], [
    Bind.Type<AppConfig>().To<IAppConfig>()
]);

var appConfig = manager.GetConfig<AppConfig>();
var appInterface = manager.GetConfig<IAppConfig>(); // ✅ Now supported!
```

---

## New Features Available After Migration

### 🆕 Zero-Config DI Integration

The new DI package works perfectly with minimal setup:

```csharp
// Just works - no additional configuration needed!
services.AddCocoarConfiguration([
    Rule.From.File("config.json").For<AppConfig>(),
    Rule.From.Environment("DB_").For<DatabaseConfig>()
]);

// Automatically registers:
// - AppConfig (Scoped)
// - DatabaseConfig (Scoped)
// - ConfigManager (Singleton)
```

### 🆕 Progressive Enhancement API

Add complexity only when needed:

```csharp
// Simple: Auto-registration only
services.AddCocoarConfiguration([rules]);

// Enhanced: Add interface bindings  
services.AddCocoarConfiguration([rules], [bindings]);

// Advanced: Full lifetime control
services.AddCocoarConfiguration([rules], [bindings], options => {
    // Custom configuration
});
```

### 🆕 Fail-Safe Design

The new API prevents common configuration mistakes:

- **Impossible to forget**: Auto-registration ensures services are always available
- **Progressive complexity**: Start simple, add features when needed  
- **Consistent patterns**: Follows ASP.NET Core DI conventions

### 🆕 Enhanced Interface Support

Clean separation between configuration and consumption:

```csharp
// Implementation (with extra properties)
public class DatabaseConfig : IDatabaseConfig
{
    public string ConnectionString { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public string InternalNotes { get; set; } = ""; // Not in interface
}

// Clean contract
public interface IDatabaseConfig  
{
    string ConnectionString { get; }
    int TimeoutSeconds { get; }
}

// Simple binding
Bind.Type<DatabaseConfig>().To<IDatabaseConfig>()
```

---

## Troubleshooting Migration Issues

### Issue: ".As() method not found"

**Cause**: Using old fluent DI API from core library.

**Solution**: Replace with new Binding + DI package pattern:
```csharp
// OLD:
Rule.From.File("config.json").For<Config>().As<IConfig>()

// NEW:
// 1. Create rule without .As():
Rule.From.File("config.json").For<Config>()
// 2. Add binding separately:
Bind.Type<Config>().To<IConfig>()
// 3. Use new DI integration
```

### Issue: "ServiceLifetime not found in rule builder"

**Cause**: ServiceLifetime parameters moved to DI package options.

**Solution**: Use options-based lifetime control:
```csharp
// OLD:
Rule.From.File("config.json").For<Config>(ServiceLifetime.Singleton)

// NEW:
services.AddCocoarConfiguration([rules], [bindings], options => {
    options.Register.Add<Config>(ServiceLifetime.Singleton);
});
```

### Issue: "AddCocoarConfiguration not found"

**Cause**: Missing DI package reference.

**Solution**: Add package and using statement:
```xml
<PackageReference Include="Cocoar.Configuration.DI" Version="1.0.0" />
```
```csharp
using Cocoar.Configuration.DI;
```

### Issue: Services registered with wrong lifetime

**Cause**: Default lifetime changed to Scoped (was Singleton in some old versions).

**Solution**: Configure default lifetime explicitly:
```csharp
services.AddCocoarConfiguration([rules], [bindings], options => {
    options.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
});
```

### Issue: Interface injection fails

**Cause**: Missing binding specification.

**Solution**: Add explicit interface binding:
```csharp
services.AddCocoarConfiguration([rules], [
    Bind.Type<ConcreteConfig>().To<IConfigInterface>()
]);
```

---

## Compatibility Notes

### ✅ Non-Breaking Changes

- **Core Rule API**: All rule creation patterns unchanged (`Rule.From.File()`, `.For<T>()`, `.Select()`, etc.)
- **Provider APIs**: File, Environment, HTTP, Static providers work identically
- **ConfigManager**: All existing methods (`GetConfig<T>()`, `Initialize()`) unchanged
- **Merge Semantics**: Configuration layering and merging behavior identical
- **Change Detection**: Provider change notifications work the same way

### ⚠️ Breaking Changes

- **DI Integration**: Must use separate `Cocoar.Configuration.DI` package
- **Fluent Interface API**: `.As<T>()` methods removed from rule builders
- **Service Lifetime**: Must use options-based lifetime control instead of fluent parameters
- **Manual DI**: Manual service registration replaced by auto-registration

### 🆕 Major Improvements

- **Zero-Config DI**: Works perfectly without any configuration
- **Interface Binding**: Clean separation via binding system works with or without DI
- **Auto-Registration**: Intelligent service registration based on rules and bindings
- **Flexible Lifetime Control**: Fine-grained control via Add/Remove methods
- **Keyed Services**: Full support for multiple registrations with service keys
- **Fail-Safe Design**: Impossible to misconfigure or forget registration steps

---

## Need Help?

- **Examples**: See [`src/Examples/`](../src/Examples/) for runnable migration examples:
  - **DIExample**: Full DI integration patterns
  - **SimplifiedCoreExample**: Pure core library usage
  - **BindingExample**: Interface binding without DI
- **Issues**: Open GitHub issues for migration questions
- **Documentation**: All docs updated to reflect new patterns

The migration provides significant improvements in usability and flexibility while maintaining the proven configuration merging engine you depend on!

---

