# CommandLine Provider

The CommandLine provider allows you to load configuration from command-line arguments with flexible, configurable argument parsing.

## Features

- **Flexible switch prefixes**: Support any prefix style (`--`, `-`, `/`, `@`, `#`, `%`, etc.) - even multiple at once!
- **Multiple formats**: Supports `--key=value`, `--key value`, and boolean flags
- **Nested configuration**: Use `:` or `__` for hierarchical keys (e.g., `--database:host=localhost`)
- **Prefix filtering**: Filter arguments by prefix to map to specific configuration types
- **Automatic fallback**: Uses `Environment.GetCommandLineArgs()` when args not explicitly provided

## Basic Usage

### Simple usage (default `--` prefix)

```csharp
builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
    rule.For<AppConfig>().FromCommandLine()
]));
```

Command line:
```bash
dotnet run --host=localhost --port=8080 --verbose
```

Maps to:
```csharp
public class AppConfig
{
    public string Host { get; set; }  // "localhost"
    public int Port { get; set; }     // 8080
    public bool Verbose { get; set; } // true
}
```

### Custom switch prefixes

Use any prefix style you prefer:

```csharp
// Single dash (Unix-style)
rule.For<AppConfig>().FromCommandLine(["-"])

// Forward slash (Windows-style)
rule.For<AppConfig>().FromCommandLine(["/"])

// Custom prefixes for semantic clarity
rule.For<AppConfig>().FromCommandLine(["@", "#"])
```

**...or literally any string you want** 😏

### Multiple switch prefixes

Accept multiple prefix styles simultaneously:

```csharp
rule.For<AppConfig>().FromCommandLine(["--", "-", "/"])
```

Command line can now mix styles:
```bash
dotnet run --host=localhost -port=8080 /verbose
```

**Note:** Prefixes are matched longest-first, so `--` is checked before `-` to avoid ambiguity.

## Advanced Usage

### Nested Configuration

Use `:` or `__` to create hierarchical configuration:

```bash
dotnet run --database:host=localhost --database:port=5432
```

Maps to:
```csharp
public class AppConfig
{
    public DatabaseConfig Database { get; set; }
}

public class DatabaseConfig
{
    public string Host { get; set; } // "localhost"
    public int Port { get; set; }    // 5432
}
```

### Using Prefix to Map Multiple Types

You can use prefixes to map different command-line arguments to different configuration types:

```csharp
builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
    rule.For<AppConfig>().FromCommandLine("app_"),
    rule.For<DatabaseConfig>().FromCommandLine("db_")
]));
```

Command line:
```bash
dotnet run --app_host=localhost --db_connectionstring="Server=localhost"
```

This maps:
- `--app_host=localhost` → `AppConfig.Host` (prefix stripped, becomes `host`)
- `--db_connectionstring=...` → `DatabaseConfig.ConnectionString` (prefix stripped, becomes `connectionstring`)

### Combining Prefix Filtering and Custom Switch Prefixes

Mix semantic prefixes with custom switch styles:

```csharp
builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
    rule.For<TargetConfig>().FromCommandLine("target_", ["@"]),
    rule.For<IssueConfig>().FromCommandLine("issue_", ["#"])
]));
```

Command line:
```bash
invoke.exe @target_host=10.10.10.10 #issue_id=123
```

### Dynamic Configuration with Config-Aware Rules

```csharp
builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
    rule.For<TenantSettings>().FromFile("tenant.json"),

    rule.For<AppConfig>().FromCommandLine(accessor =>
    {
        var tenant = accessor.GetRequiredConfig<TenantSettings>();
        return new CommandLineRuleOptions
        {
            Prefix = $"{tenant.Name}_",
            SwitchPrefixes = ["--", "-"]
        };
    })
]));
```

## Argument Format Support

The provider supports multiple argument formats:

| Format | Example | Result |
|--------|---------|--------|
| `--key=value` | `--host=localhost` | `{ "host": "localhost" }` |
| `--key value` | `--host localhost` | `{ "host": "localhost" }` |
| `--flag` | `--verbose` | `{ "verbose": "true" }` |
| `--nested:key` | `--db:host=localhost` | `{ "db": { "host": "localhost" } }` |
| `--nested__key` | `--db__host=localhost` | `{ "db": { "host": "localhost" } }` |

**Custom prefixes:** All formats work with any configured switch prefix (`-`, `/`, `@`, etc.)

## Configuration Options

### Simple API

```csharp
// Default (-- prefix, no filtering)
.FromCommandLine()

// With prefix filtering only
.FromCommandLine("app_")

// With custom switch prefix
.FromCommandLine(["-"])

// With multiple switch prefixes
.FromCommandLine(["--", "-", "/"])

// Prefix filtering + custom switches
.FromCommandLine("app_", ["@", "#"])
```

### Factory API (for testing/advanced scenarios)

```csharp
.FromCommandLine(cm => new CommandLineRuleOptions
{
    Args = testArgs,              // For testing; defaults to Environment.GetCommandLineArgs()
    SwitchPrefixes = ["@", "#"],  // Custom switch prefixes; defaults to ["--"]
    Prefix = "app_"               // Prefix filter; defaults to null (no filtering)
})
```

## Type Conversion

The ConfigurationManager automatically converts string values to the target property types:

```bash
dotnet run --port=8080 --timeout=30.5 --enabled=true
```

```csharp
public class AppConfig
{
    public int Port { get; set; }        // Converted to int: 8080
    public double Timeout { get; set; }  // Converted to double: 30.5
    public bool Enabled { get; set; }    // Converted to bool: true
}
```

## Layering with Other Providers

Command-line arguments are typically used as the highest-priority layer to override file and environment-based configuration:

```csharp
builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rule => [
    rule.For<AppConfig>().FromFile("appsettings.json"),  // Base
    rule.For<AppConfig>().FromEnvironment("APP_"),       // Override
    rule.For<AppConfig>().FromCommandLine()              // Final override
]));
```

Command line:
```bash
dotnet run --port=9000
```

This overrides the port from both the file and environment variables.

## Creative Use Cases

### Semantic Prefixes for Self-Documenting CLIs

```csharp
rule.For<TargetConfig>().FromCommandLine(["@"]),
rule.For<IssueConfig>().FromCommandLine(["#"]),
rule.For<EnvConfig>().FromCommandLine(["%"])
```

```bash
invoke.exe @host=10.10.10.10 #ticket=456 %env=prod
```

### Mixed Unix/Windows Style

Accept both Unix and Windows conventions:

```csharp
rule.For<AppConfig>().FromCommandLine(["--", "/"])
```

```bash
dotnet run --host=localhost /port=8080
```

## Limitations

- **No reactive updates**: Command-line arguments are static; they don't change during application lifetime
- **Basic parsing only**: No support for subcommands, argument validation, or complex parsing rules
- **String-based**: All values are initially strings and must be convertible to target property types

## Use Cases

- **Development overrides**: Quickly override configuration during development
- **Container/deployment**: Pass environment-specific values at runtime (e.g., `docker run ... --port=8080`)
- **Testing**: Inject test configuration without modifying environment or files
- **Self-documenting CLIs**: Use semantic prefixes (`@host`, `#issue`) for clarity

## See Also

- [Environment Variable Provider](../EnvironmentVariableProvider/README.md)
- [File Source Provider](../FileSourceProvider/README.md)
