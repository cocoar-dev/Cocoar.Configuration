---
description: "FromTomlFile provider (Cocoar.Configuration.Toml) — reactive .toml watching, TOML typed values (string/int/float/bool/datetime/array/table) mapped to JSON, arrays-of-tables, Kubernetes ConfigMap support"
---

# TOML Provider

`Cocoar.Configuration.Toml` reads `.toml` files into the configuration pipeline, with the same reactive file-watching, path resolution, and security as the [File provider](/guide/providers/file) — including `followSymlinks: true` for [Kubernetes ConfigMap / Secret mounts](/guide/providers/file#kubernetes-configmap-secret-mounts). Opt-in package (it takes a Tomlyn dependency).

```shell
dotnet add package Cocoar.Configuration.Toml
```

```csharp
using Cocoar.Configuration.Toml;

builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules =>
    [
        rules.For<AppSettings>().FromTomlFile("appsettings.toml"),
    ]));
```

## Typed values

TOML is strongly typed, so the mapping to JSON is unambiguous — no scalar-style guessing as in YAML:

| TOML | Binds as |
|---|---|
| `name = "hello"` | string |
| `enabled = true` | boolean |
| `port = 5432` | number |
| `ratio = 1.5` | number |
| `created = 1979-05-27T07:32:00Z` | string (ISO-8601) |
| `hosts = ["a", "b"]` | array |
| `[db]` (table) | object |
| `[[servers]]` (array of tables) | array of objects |

Date/time values are emitted as ISO-8601 strings; the binder coerces them to `DateTime` / `DateTimeOffset` as needed.

## Reactivity & per-tenant paths

Editing the file triggers a recompute (same watcher as `FromFile`). A config-aware overload resolves the path per recompute — e.g. per tenant:

```csharp
rules.For<Branding>().FromTomlFile(a => $"tenants/{a.Tenant}/config.toml").TenantScoped()
```

## Other formats

For `.yaml` / `.yml` see the [YAML provider](/guide/providers/yaml); for `.env` see [Dotenv](/guide/providers/dotenv); for `.ini` see the [INI provider](/guide/providers/ini).
