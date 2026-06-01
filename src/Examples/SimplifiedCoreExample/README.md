# Simplified Core Example

This example demonstrates the **simplified core API** after removing DI constructs from `Cocoar.Configuration`.

## What This Shows

- **Pure concrete type configuration**: Only `For<ConcreteType>()` - no interfaces, no lifetimes
- **Manual ConfigManager usage**: No DI integration - direct instantiation and retrieval
- **Rule layering**: Later rules override earlier ones (last-write-wins semantics)
- **Multiple file sources**: Separate JSON files for different configuration areas

## Key API Changes

### ✅ Simplified Rule Building
```csharp
// OLD (with DI/interface concerns mixed in)
Rule.From.File("app.json").For<AppConfig>().As<IAppConfig>(ServiceLifetime.Singleton)

// NEW (core responsibility only)
Rule.From.File("app.json").For<AppConfig>()
```

### ✅ Direct Manager Usage
```csharp
// Manual configuration manager (no DI)
var manager = ConfigManager.Create(c => c.UseConfiguration(rules));
var config = manager.GetConfig<AppConfig>();
```

### ❌ Removed Features (moved to DI package)
- `.As<TInterface>()` - interface exposure
- `ServiceLifetime` parameters - DI lifetimes
- `AddCocoarConfiguration()` - DI integration
- Service keys - keyed DI registrations

## Running the Example

```bash
cd src/Examples/SimplifiedCoreExample
dotnet run
```

## File Structure

```
config/
├── app.json        # Application metadata
├── database.json   # Database connection settings
└── features.json   # Feature toggles
```

## Expected Output

The example will:
1. Load three separate configuration objects from JSON files
2. Display their contents in a formatted way
3. Demonstrate rule layering with static JSON override
4. Show the key API patterns and what's changed

## Migration Notes

This represents the **core library after DI separation**. For projects needing:
- **DI integration**: Use `Cocoar.Configuration.DI` package
- **Interface exposure**: Future `TypeExposureRegistry` (see proposal docs)
- **ASP.NET Core**: Use `Cocoar.Configuration.AspNetCore` package

## Purpose

- Test the simplified core API without breaking existing examples
- Validate that basic configuration loading still works without DI
- Document the new mental model: rules are purely about data acquisition
- Provide a reference for core-only usage scenarios
