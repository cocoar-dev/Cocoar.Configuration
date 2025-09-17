# Binding System Example

Focused demonstration of interface binding without using a DI container.

Central documentation: see [docs/BINDING.md](../../docs/BINDING.md) for full concepts, guidelines, troubleshooting, and DI integration patterns.

What this example shows:
- Rules producing a concrete config type
- Binding that maps the concrete type to an interface
- Access via both concrete and interface (`ConfigManager.GetConfig<T>()`)

Run:
```bash
dotnet run
```

See also:
- `Examples/DIExample` for bindings + DI auto-registration
- `Examples/ServiceLifetimes` for lifetime & keyed service control