---
description: "FromDotEnv provider (core, no dependency) — .env KEY=value parsing, # comments, export prefix, single/double quotes, inline comments, :/__ key nesting, reactive file-watching"
---

# Dotenv (.env) Provider

`FromDotEnv` reads a 12-factor-style `.env` file into the configuration pipeline. It is **built into the core package** (no extra dependency) and uses the same reactive file-watching as the [File provider](/guide/providers/file) — including `followSymlinks: true` for [Kubernetes ConfigMap / Secret mounts](/guide/providers/file#kubernetes-configmap-secret-mounts).

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules =>
    [
        rules.For<AppSettings>().FromDotEnv(),          // defaults to ".env"
        rules.For<AppSettings>().FromDotEnv("local.env"),
    ]));
```

## Format

```shell
# comments and blank lines are ignored
NAME=myapp
export TOKEN=abc123        # an optional `export` prefix is stripped

DQ="hello world"           # double quotes; supports \n \t \" \\ escapes
SQ='literal $x'            # single quotes are literal
INLINE=value # trailing comment   (stripped — needs a leading space)

# Nested keys with : or __  (like environment variables)
Db__Port=5432              # → { "Db": { "Port": "5432" } }
Db:Host=localhost
```

Values are emitted as strings; the binder coerces them to the target type (e.g. `Db__Port=5432` binds to an `int`). Keys nest on `:` or `__`, matching the [Environment Variables provider](/guide/providers/environment).

## Reactivity & per-tenant paths

Editing the file triggers a recompute. A config-aware overload resolves the path per recompute:

```csharp
rules.For<Secrets>().FromDotEnv(a => $"tenants/{a.Tenant}/.env").TenantScoped()
```

## YAML?

For `.yaml` / `.yml` files see the [YAML provider](/guide/providers/yaml) (`Cocoar.Configuration.Yaml`).
