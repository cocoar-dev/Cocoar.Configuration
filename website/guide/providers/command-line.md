---
description: "FromCommandLine provider, switch-prefix filtering, key=value/key value/boolean-flag formats, : and __ nesting, custom prefixes, highest-priority override"
---

# Command Line Provider

The command line provider parses command-line arguments into JSON configuration.

```csharp
rule.For<AppSettings>().FromCommandLine("--app:")
```

## How It Works

1. Scans arguments for entries matching the switch prefix (default `--`)
2. Parses key-value pairs from the matched arguments
3. Converts flat keys to nested JSON (same nesting rules as environment variables)

This provider is **static** — command-line arguments don't change during process lifetime.

## Argument Formats

The parser supports several formats:

```shell
# Key=value (single argument)
--MaxRetries=10

# Key value (two arguments)
--MaxRetries 10

# Boolean flag (no value = "true")
--EnableLogging

# Nested keys with : or __
--Database:Host=localhost
--Database__Port=5432
```

::: warning Values that start with a switch prefix
In the two-argument `--key value` form, a value that itself begins with a switch prefix (e.g. `--port -5`)
is parsed as a **boolean flag** (`--port` → `true`), not as the value `-5`. Use the `=` form for such
values: `--port=-5`.
:::

## Prefix Filtering

Filter arguments by a prefix to avoid collisions:

```csharp
// Only arguments starting with "--app:"
rule.For<AppSettings>().FromCommandLine("--app:")
```

```shell
dotnet run --app:MaxRetries=10 --app:Debug=true --other:Ignored=yes
```

Produces `{ "MaxRetries": 10, "Debug": true }` for `AppSettings`. Arguments starting with `--other:` are ignored.

## Custom Switch Prefixes <Badge type="info" text="ADV" />

By default, the parser looks for `--`. You can add other prefixes:

```csharp
// Accept both - and -- as switch prefixes
rule.For<AppSettings>().FromCommandLine(["-", "--"])
```

```shell
dotnet run -MaxRetries=10 --Debug=true
```

When multiple prefixes match, the longest prefix wins.

## Common Pattern

Command-line arguments as the highest-priority override:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json").Required(),
    rule.For<AppSettings>().FromEnvironment("APP_"),
    rule.For<AppSettings>().FromCommandLine("--app:"),  // Highest priority
]
```

## Dynamic Options <Badge type="info" text="ADV" />

Use the factory overload for custom parsing:

```csharp
rule.For<AppSettings>().FromCommandLine(accessor =>
    new CommandLineRuleOptions(
        Args: args,
        SwitchPrefixes: ["--", "-"],
        Prefix: "app"))
```
