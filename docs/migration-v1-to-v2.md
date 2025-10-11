# Migration Guide: v1.1.0 → v2.0.0

This guide explains how to migrate from v1.1.0 to v2.0.0, which introduces function-based builder APIs for both rules and setup configuration.

## 1. Overview

| v1.x API | v2.0 API |
|----------|----------|
| `Rule.From.File(...)` | `rule => rule.File(...)` |
| `Rule.From.Environment(...)` | `rule => rule.Environment(...)` |
| `Bind.Type<My>().To<IMy>()` | `setup => setup.ConcreteType<My>().ExposeAs<IMy>()` |
| `ServiceRegistrationOptions` | Removed (use fluent lifetime methods) |
| Array of rules `[Rule.From...]` | Lambda builder `rule => [rule.File(...)]` |
| Array of bindings `[Bind.Type...]` | Lambda builder `setup => [setup.ConcreteType(...)]` |

The core package remains DI-agnostic. DI-specific behavior lights up only when the `Cocoar.Configuration.DI` package is referenced.

## 2. Quick Examples

### Before (v1.x)
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("config.json").Select("App").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
], [
    Bind.Type<AppSettings>().To<IAppSettings>()
]);
```

### After (v2.0)
```csharp
services.AddCocoarConfiguration(rule => [
    rule.File("config.json").Select("App").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

### Non-DI (core only) - Before
```csharp
var manager = new ConfigManager([
    Rule.From.File("config.json").For<AppSettings>()
], [
    Bind.Type<AppSettings>().To<IAppSettings>()
]);
```

### Non-DI (core only) - After
```csharp
var manager = new ConfigManager(rule => [
    rule.File("config.json").For<AppSettings>()
], setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

## 3. Key Concepts

### Rules Builder Function
You pass a lambda that receives a `RulesBuilder` instance (parameter named `rule`). Inside this lambda, you call methods on the builder to define configuration rules.

```csharp
// The 'rule' parameter is a RulesBuilder instance
services.AddCocoarConfiguration(rule => [
    rule.File("config.json").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
], ...);
```

### Setup Builder Function
You pass a lambda that receives a `SetupBuilder` instance (parameter named `setup`). Inside this lambda, you call methods on the builder to configure type exposures.

```csharp
// The 'setup' parameter is a SetupBuilder instance
services.AddCocoarConfiguration(..., setup => [
    setup.ConcreteType<MyType>().ExposeAs<IMyInterface>()
]);
```

### Building Rules
- `rule.File(...)` - File-based configuration
- `rule.Environment(...)` - Environment variable configuration
- `rule.Static(...)` - Static object or factory function
- `rule.StaticJson(...)` - Static JSON string
- `rule.Observable(...)` - Observable-based dynamic configuration
- `rule.FromProvider<...>(...)` - Generic provider API

### Building Concrete Types
`setup.ConcreteType<T>()` creates a builder for a concrete config type `T`.

### Exposing Interfaces
`.ExposeAs<IInterface>()` records that `T` implements `IInterface`. Without DI, this just influences exposure resolution. With DI, both the concrete type and interface are registered as Scoped.

### Lifetimes
All registrations (concrete + exposed interfaces) are Scoped by default. No global overrides.

### Keys
Removed. Simplicity > flexibility for initial release.

### Reactive Config Registration
Always available. `IReactiveConfig<T>` is registered for each concrete type automatically.

## 4. Migration Steps

### Step 1: Replace Rule Arrays with Lambda
**Before:**
```csharp
[
    Rule.From.File("config.json").For<AppSettings>(),
    Rule.From.Environment("APP_").For<AppSettings>()
]
```

**After:**
```csharp
rule => [
    rule.File("config.json").For<AppSettings>(),
    rule.Environment("APP_").For<AppSettings>()
]
```

### Step 2: Update Rule Methods
Replace `Rule.From.*` static methods with `rule.*` instance methods:
- `Rule.From.File(...)` → `rule.File(...)`
- `Rule.From.Environment(...)` → `rule.Environment(...)`
- `Rule.From.StaticJson(...)` → `rule.StaticJson(...)`
- `Rule.From.HttpPolling(...)` → Use provider API

### Step 3: Replace Bind Arrays with Lambda
**Before:**
```csharp
[
    Bind.Type<AppSettings>().To<IAppSettings>()
]
```

**After:**
```csharp
setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]
```

### Step 4: Update Method Names
- `Bind.Type<T>()` → `setup.ConcreteType<T>()`
- `.To<IInterface>()` → `.ExposeAs<IInterface>()`

### Step 5: Clean Up
- Remove any lifetime override code (no longer applicable)
- Remove keyed service registrations (not supported)
- Remove reactive registration opt-in/out calls (always registered)
- Remove all references to `ServiceRegistrationOptions` and related types

## 5. FAQ

**Q: Why did the API change to lambdas?**  
A: The lambda-based builder pattern is more intuitive, reduces ceremony, and allows for better IDE autocomplete support.

**Q: Do I still need to supply bindings to `AddCocoarConfiguration`?**  
A: Yes, but they're now defined using a lambda: `setup => [setup.ConcreteType<T>()...]`

**Q: Can I make a config Singleton?**  
A: Not in the simplified model. Everything is Scoped by design (per web request, etc.).

**Q: How do I disable registration for a config type?**  
A: Call `.DisableAutoRegistration()` on its concrete builder inside the setup lambda.

**Q: How do I stop an interface from being registered?**  
A: `setup.ExposedType<IMyInterface>().DisableAutoRegistration();`

**Q: How do I get a reactive config?**  
A: Inject `IReactiveConfig<MySettings>`; it's always registered.

**Q: Is there a static `Rule.From` or `Configure` class anymore?**  
A: No! Use the lambda parameters: `rule.File(...)` and `setup.ConcreteType<>()`

**Q: What's the difference between `rule` and `rules`?**  
A: `rule` is the parameter name (singular) because you're building one rule at a time inside the lambda.

## 6. Reference Cheat Sheet
```csharp
// Inside your configure function:
services.AddCocoarConfiguration(rules, setup => [
    setup.ConcreteType<My>()
        .ExposeAs<IMy>(),             // both Scoped

    setup.ConcreteType<MyOther>()
        .DisableAutoRegistration(),   // no DI registration

    setup.ExposedType<IMy>().DisableAutoRegistration() // global interface opt-out
]);
```
A: Inject `IReactiveConfig<MySettings>`; it’s always registered.

## 6. Reference Cheat Sheet
```csharp
setup.ConcreteType<My>()
    .ExposeAs<IMy>();             // both Scoped

setup.ConcreteType<MyOther>()
    .DisableAutoRegistration();   // no DI registration

setup.ExposedType<IMy>().DisableAutoRegistration(); // global interface opt-out
```

## 7. Removing Legacy Code

### Removed Classes
- `Rule` static class (replaced by `RulesBuilder` instance)
- `Bind` static class (replaced by `SetupBuilder` instance)
- `IServiceRegistration.cs`
- `ServiceRegistration.cs`
- `Register.cs`
- `ServiceRegistrationOptions.cs`

### Removed Methods
- `Rule.From.File()`
- `Rule.From.Environment()`
- `Rule.From.StaticJson()`
- `Bind.Type<T>()`

## 8. Validation Tips

After migration, verify:
- All configuration rules compile and use `rule =>` lambda
- All setup/bindings use `setup =>` lambda
- No references to `Rule.From.*` or `Bind.Type<>()`
- Tests pass with new API
- IDE autocomplete works inside lambdas

### Common Compiler Errors
```
Error: 'Rule' does not contain a definition for 'From'
→ Change Rule.From.File(...) to rule => rule.File(...)

Error: 'Bind' does not contain a definition for 'Type'
→ Change Bind.Type<T>() to setup => setup.ConcreteType<T>()
```

## 9. Additional Resources

- [README.md](../README.md) - Updated examples with v2.0 API
- [Examples folder](../src/Examples/) - All 11 examples updated to v2.0
- [CHANGELOG.md](../CHANGELOG.md) - Full list of breaking changes

---
Migration complete! 🚀 Welcome to Cocoar.Configuration v2.0


