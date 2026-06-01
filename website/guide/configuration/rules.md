---
description: Rule anatomy with For<T>().FromFile, top-to-bottom property-by-property JSON merge layering, last-write-wins, and Select to extract a sub-document
---

# Rules & Layering

Rules are the central concept in Cocoar.Configuration. A rule connects a **configuration type** to a **data source** and defines how they interact.

## Anatomy of a Rule

Every rule starts with a type and a provider:

```csharp
rule.For<AppSettings>()              // What type to populate
    .FromFile("appsettings.json")    // Where to get the data
```

This creates a rule that reads `appsettings.json` and deserializes it into `AppSettings`.

## How Layering Works

Multiple rules for the same type merge their JSON, property by property:

```csharp
rule => [
    rule.For<AppSettings>().FromFile("appsettings.json"),       // Rule 1: base
    rule.For<AppSettings>().FromFile("appsettings.local.json"), // Rule 2: overrides
    rule.For<AppSettings>().FromEnvironment("APP_"),            // Rule 3: final overrides
]
```

Rules execute **top to bottom**. When two rules set the same property, the later one wins. Properties not set by later rules keep their value from earlier ones.

**Example:**

**appsettings.json** (Rule 1):
```json
{ "AppName": "MyApp", "MaxRetries": 3, "Debug": false }
```

**appsettings.local.json** (Rule 2):
```json
{ "MaxRetries": 10 }
```

```shell
# Environment variable (Rule 3)
APP_Debug=true
```

**Result:** `{ AppName: "MyApp", MaxRetries: 10, Debug: true }`

Each rule contributes only the properties it provides. The merge happens at the JSON level before deserialization.

## Select — Extract a Sub-Document

When your JSON file contains multiple config sections, use `.Select()` to pick one:

```json
{
  "App": {
    "Name": "MyApp",
    "Version": "1.0"
  },
  "Database": {
    "Host": "localhost",
    "Port": 5432
  }
}
```

```csharp
rule => [
    rule.For<AppConfig>().FromFile("appsettings.json").Select("App"),
    rule.For<DatabaseConfig>().FromFile("appsettings.json").Select("Database"),
]
```

`.Select("App")` extracts the `App` object from the JSON before deserializing. The file is shared but each type gets its own section.

Nested paths use colon notation: `.Select("App:Logging:Level")`.

## MountAt — Nest Under a Path <Badge type="info" text="ADV" />

The opposite of Select. MountAt places the provider's output under a JSON path before merging:

```csharp
// Provider returns: { "Host": "localhost", "Port": 5432 }
rule.For<AppConfig>().FromFile("db-override.json").MountAt("Database")
// Merged as: { "Database": { "Host": "localhost", "Port": 5432 } }
```

This is useful when a config file contains flat values that should map to a nested property on your type.

## Named — Label Rules for Health Monitoring <Badge type="info" text="ADV" />

Give rules human-readable names for observability:

```csharp
rule.For<AppSettings>()
    .FromFile("appsettings.json")
    .Named("Base App Settings")
```

The name appears in health snapshots, making it easy to identify which rule failed in dashboards and logs.

## Multiple Types, One Rule List

All configuration types are defined in the same rule list:

```csharp
ConfigManager.Create(c => c
    .UseConfiguration(rule => [
        // AppSettings from file + env
        rule.For<AppSettings>().FromFile("appsettings.json"),
        rule.For<AppSettings>().FromEnvironment("APP_"),

        // DatabaseConfig from file only
        rule.For<DatabaseConfig>().FromFile("appsettings.json").Select("Database"),

        // FeatureConfig from a remote endpoint
        rule.For<FeatureConfig>().FromHttp("https://config.example.com/features",
            pollInterval: TimeSpan.FromMinutes(5)),
    ]));
```

Rule ordering matters within the same type (for layering), but rules for different types are independent of each other.

## How Recompute Works <Badge type="info" text="ADV" />

When a source changes (file modified, HTTP poll returns new data, etc.):

1. The change is **debounced** (default 300ms) to coalesce rapid changes
2. All rules **re-execute** starting from the earliest changed rule
3. JSON is **re-merged** for affected types
4. Types are **re-deserialized** into new instances
5. If all required rules succeed, the new snapshot **atomically replaces** the old one
6. Subscribers receive the updated values

The old snapshot remains available until the new one is fully built. There is no moment where partially-updated config is visible.
