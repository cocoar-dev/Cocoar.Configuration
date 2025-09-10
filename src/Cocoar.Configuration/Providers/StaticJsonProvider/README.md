# StaticJsonProvider

Seed a configuration type with a value produced by a factory.

- Instance options: `StaticJsonProviderOptions(Func<ConfigManager, object> Factory)`
- Query options: `StaticJsonProviderQueryOptions(wrapperPath?)`
- Changes: none (static); the factory is evaluated during recompute.

Fluent usage:

```csharp
services.AddCocoarConfiguration(
    Rules.FromStatic(_ => new MySettings { Url = "/api/config" })
         .ForType<MySettings>()
         .Required()
         .Build()
);
```
