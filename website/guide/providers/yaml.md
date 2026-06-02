---
description: "FromYamlFile provider (Cocoar.Configuration.Yaml) — reactive .yaml/.yml watching, YAML core-schema scalar type-inference (bool/number/null), quoted/block scalars stay strings"
---

# YAML Provider

`Cocoar.Configuration.Yaml` reads `.yaml` / `.yml` files into the configuration pipeline, with the same reactive file-watching, path resolution, and security as the [File provider](/guide/providers/file). Opt-in package (it takes a YamlDotNet dependency).

```shell
dotnet add package Cocoar.Configuration.Yaml
```

```csharp
using Cocoar.Configuration.Yaml;

builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules =>
    [
        rules.For<AppSettings>().FromYamlFile("appsettings.yaml"),
    ]));
```

## Scalar types

Plain (unquoted) scalars are mapped to their JSON types, so a YAML file binds exactly like the equivalent JSON:

| YAML | Binds as |
|---|---|
| `enabled: true` | boolean |
| `port: 5432` | number |
| `ratio: 1.5` | number |
| `note: null` (or `~`) | null |
| `name: hello` | string |
| `note: "true"` | **string** (quoted) |

Quoted (`"…"` / `'…'`) and block (`|`, `>`) scalars are always strings.

## Reactivity & per-tenant paths

Editing the file triggers a recompute (same watcher as `FromFile`). A config-aware overload resolves the path per recompute — e.g. per tenant:

```csharp
rules.For<Branding>().FromYamlFile(a => $"tenants/{a.Tenant}/branding.yaml").TenantScoped()
```

## Looking for `.env`?

The dotenv provider (`FromDotEnv`) is built into the **core** package — see [Dotenv](/guide/providers/dotenv).
