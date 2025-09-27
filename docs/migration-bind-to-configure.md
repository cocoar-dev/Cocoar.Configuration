# Migration Guide: Bind → Configure (Simplified API)

This guide explains how to migrate from the legacy `Bind` + `ServiceRegistrationOptions` APIs to the new unified `Configure` API.

## 1. Overview

| Legacy | New |
|--------|-----|
| `Bind.Type<My>() .To<IMy>()` | `setup => setup.ConcreteType<My>().ExposeAs<IMy>()` |
| `ServiceRegistrationOptions.DefaultRegistrationLifetime(...)` | Removed (all registrations are Scoped) |
| `options.Register.Add<T>(lifetime, key)` | Removed (keys & lifetime overrides dropped) |
| Auto reactive config toggle (`DisableAutoReactiveRegistration`) | Removed (reactive always available) |
| Global lifetime defaults | Fixed Scoped default |

The core package remains DI-agnostic. DI-specific behavior lights up only when the `Cocoar.Configuration.DI` package is referenced.

## 2. Quick Examples

### Before
```csharp
var bindings = new [] {
    Bind.Type<AppSettings>().To<IAppSettings>()
};
services.AddCocoarConfiguration(rules, bindings, options => {
    options.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
    options.Register.Add<IAppSettings>(ServiceLifetime.Scoped, "alt");
});
```

### After
```csharp
services.AddCocoarConfiguration(rules, setup => [
    setup.ConcreteType<AppSettings>()
        .ExposeAs<IAppSettings>() // Concrete + interface both Scoped
]);
```

### Non-DI (core only)
```csharp
var manager = new ConfigManager(rules, setup => [
    setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>()
]);
```

## 3. Key Concepts

### Configure Function
You pass a function that receives a `SetupBuilder` instance. Inside this function, you call methods on the builder to configure your types.

```csharp
// The 'configure' parameter is a SetupBuilder instance
services.AddCocoarConfiguration(rules, setup => [
    setup.ConcreteType<MyType>().ExposeAs<IMyInterface>()
]);
```

### Building Concrete Types
`setup.ConcreteType<T>()` creates a builder for a concrete config type `T`.

### Exposing Interfaces
`.ExposeAs<IInterface>()` records that `T` implements `IInterface`. Without DI, this just influences exposure resolution. With DI, both the concrete type and interface are registered as Scoped.

### Lifetimes
All registrations (concrete + exposed interfaces) are Scoped. No overrides.

### Keys
Removed. Simplicity > flexibility for initial release.

### Reactive Config Registration
Always available. `IReactiveConfig<T>` is registered for each concrete type automatically.

## 4. Migration Steps
1. Replace each `Bind.Type<T>().To<I1>().To<I2>()` with a function-based configure:
   ```csharp
   services.AddCocoarConfiguration(rules, setup => [
       setup.ConcreteType<T>()
           .ExposeAs<I1>()
           .ExposeAs<I2>()
   ]);
   ```
2. Remove any lifetime override code (no longer applicable).
3. Remove keyed service registrations (not supported).
4. Remove reactive registration opt-in/out calls (always registered).
5. Remove all references to `ServiceRegistrationOptions` and related types.
6. Wrap your configuration in a function: `setup => [ ... ]`

## 5. FAQ
**Q: Do I still need to supply bindings to `AddCocoarConfiguration`?**  
A: No legacy bindings. Use the `configure` function parameter.

**Q: Can I make a config Singleton?**  
A: Not in the simplified model. Everything is Scoped by design (per web request, etc.).

**Q: How do I disable registration for a config type?**  
A: Call `.DisableAutoRegistration()` on its concrete builder inside the configure function.

**Q: How do I stop an interface from being registered?**  
A: `setup.ExposedType<IMyInterface>().DisableAutoRegistration();`

**Q: How do I get a reactive config?**  
A: Inject `IReactiveConfig<MySettings>`; it's always registered.

**Q: Is there a static `Configure` class I can call?**  
A: No! The `configure` parameter is passed to your function. Use it as `setup.ConcreteType<>()`.

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
Legacy files removed (or excluded):
Removed legacy DI surface:
- IServiceRegistration.cs
- ServiceRegistration.cs
- Register.cs
- ServiceRegistrationOptions.cs

## 8. Validation Tips
- Ensure all tests referencing `Bind` compile (replace them or keep `Bind` only as an obsolete shim until fully removed).
- Add tests for:
- Interface exposure presence / opt-out via `DisableAutoRegistration`.
- Reactive tuple correctness.

## 9. Future Enhancements
- Possible caching / descriptor snapshot for applying multiple builders at once.
- Optional analyzer to flag lingering `Bind` usage.

---
Migration complete. ✅


