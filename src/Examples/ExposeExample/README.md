# Exposure Example

Focused demonstration of exposing a concrete configuration type as one or more interfaces without using a DI container.

See the main README and [docs/migration-v1-to-v2.md](../../docs/migration-v1-to-v2.md) for details about the Configure API and interface exposure.

What this example shows:
- Rules producing a concrete config type
- Exposure that maps the concrete type to an interface
- Access via both concrete and interface (`ConfigManager.GetConfig<T>()`)

Run:
```bash
dotnet run
```
