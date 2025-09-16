# Binding System

The Binding System maps concrete configuration types produced by rules to one or more interfaces, enabling clean contracts without forcing a DI container.

---
## Why Bind?
- Encapsulation: Hide internal or writable members
- Narrow Interfaces: Provide read-only or subset projections
- Multiple Views: One concrete type can back several interfaces
- Late Introduction: Add bindings only when consumers appear that need them

Bindings are optional. You can:
1. Use concrete types only
2. Add bindings without DI (pure `ConfigManager` usage)
3. Combine bindings + DI integration for automatic interface injection

---
## Core Concepts

| Concept | Description |
|---------|-------------|
| Rule | Defines source + (optional) selection + target concrete config type |
| Binding | Interface mapping for a concrete type (`Bind.Type<TConcrete>().To<IInterface>()`) |
| Binding Registry | Internal map: Interface → Concrete Type (one-to-one per interface) |
| Resolution | `GetConfig<IFoo>()` → registry lookup → underlying concrete snapshot instance |

---
## Minimal Usage (No DI)
```csharp
var manager = new ConfigManager([
    Rule.From.File("payment.json").For<PaymentConfig>()
], [
    Bind.Type<PaymentConfig>().To<IPaymentConfig>()
]).Initialize();

var concrete = manager.GetConfig<PaymentConfig>();
var contract = manager.GetConfig<IPaymentConfig>();
```

---
## Multiple Interfaces
```csharp
Bind.Type<FeatureConfig>()
    .To<IFeatureFlags>()
    .To<IReadOnlyFeatureFlags>();
```
All interface resolutions return the same underlying snapshot instance; no extra materialization.

---
## Validation & Safety
- On resolution, the system verifies the concrete type implements the interface
- Misconfigurations surface early (exception thrown)
- Bindings are immutable once the manager is constructed

---
## With DI Integration
```csharp
services.AddCocoarConfiguration([
    Rule.From.File("feature.json").For<FeatureConfig>()
], [
    Bind.Type<FeatureConfig>().To<IFeatureFlags>().To<IReadOnlyFeatureFlags>()
]);
// Auto-registers: FeatureConfig + IFeatureFlags + IReadOnlyFeatureFlags
```
Customize service lifetimes or disable auto-registration:
```csharp
services.AddCocoarConfiguration([rules], [bindings], opts => {
    opts.DefaultRegistrationLifetime(ServiceLifetime.Singleton);
    opts.Register.Add<IFeatureFlags>(ServiceLifetime.Transient, "test");
});
```

---
## Design Guidelines
- Start concrete-first; add bindings when you see repetitive property usage patterns
- Prefer read-only interfaces for consumer code paths
- Group interface sets logically (e.g. `IConnectionConfig`, `ICacheConfig`)
- Avoid binding two concrete types to the same interface (conflict guarded)

---
## When Not To Bind
- Single consumer scenario
- Short-lived prototype / POC
- When direct property mutation is intentionally part of the usage contract

---
## Example Projects
- `Examples/BindingExample` – Pure binding without DI
- `Examples/DIExample` – Binding + DI lifetimes
- `Examples/ServiceLifetimes` – Keyed & advanced registration scenarios

---
## Troubleshooting
| Symptom | Cause | Fix |
|---------|-------|-----|
| `null` for interface | No binding provided | Add `Bind.Type<T>().To<I>()` |
| Exception: interface not implemented | Binding declared but type mismatch | Fix concrete class or remove binding |
| DI injection missing interface | Binding added but no DI package | Add `Cocoar.Configuration.DI` package |
| Lifetime unexpected | Default changed / overridden | Use `opts.DefaultRegistrationLifetime(...)` |

---
## Related Docs
- [Concepts](CONCEPTS.md)
- [Advanced Features](ADVANCED.md)
- [Migration Guide](MIGRATION.md)
