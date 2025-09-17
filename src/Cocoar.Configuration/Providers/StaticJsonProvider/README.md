# StaticJsonProvider

Seed a configuration type with static values from JSON data or factory functions.

## Features

- **JSON String Support**: Directly provide JSON strings for configuration
- **Factory Functions**: Generate configurations dynamically using `ConfigManager` dependencies
- **No Instance Sharing**: Each rule gets its own isolated provider instance
- **Change Signal**: None (static data)

## API Options

- **Instance options**: `StaticJsonProviderOptions(JsonElement)` or factory-based
- **Query options**: `StaticJsonProviderQueryOptions(wrapperPath?)`

## Usage Examples

### JSON String (New!)

```csharp
// Direct JSON string
var rule = Rule.From.StaticJson("""
{
    "DatabaseUrl": "Server=localhost;Database=MyApp",
    "Timeout": 30,
    "EnableRetries": true
}
""").For<DatabaseSettings>();

services.AddCocoarConfiguration([rule]);
```

### Factory Function

```csharp
// Dynamic factory-based configuration
var rule = Rule.From.Static<MySettings>(_ => new MySettings { 
    Url = "/api/config",
    GeneratedAt = DateTime.UtcNow
}).For<MySettings>();

services.AddCocoarConfiguration([rule]);
```

### Static Method API

```csharp
// Using static methods directly
var jsonRule = StaticJsonProvider.CreateRule<DatabaseSettings>("""
{
    "DatabaseUrl": "Server=prod;Database=MyApp",
    "Timeout": 45
}
""");

var factoryRule = StaticJsonProvider.CreateRule<MySettings>(
    _ => new MySettings { Environment = "Production" }
);
```

## Key Benefits

- **Simple Testing**: Easy to provide test configurations via JSON strings
- **Configuration Overrides**: Layer static values over file/environment config
- **Dependency Composition**: Use factory functions to build config from other resolved configs
- **Type Safety**: Strongly typed configuration objects with compile-time checking
