---
description: "FromIniFile provider (core, no dependency) — .ini [section] headers, key=value, ;/# whole-line comments, :/. nesting, quote stripping, connection-string-safe (no inline-comment stripping), reactive watching"
---

# INI Provider

`FromIniFile` reads a classic `.ini` file into the configuration pipeline. It is **built into the core package** (no extra dependency) and uses the same reactive file-watching as the [File provider](/guide/providers/file) — including `followSymlinks: true` for [Kubernetes ConfigMap / Secret mounts](/guide/providers/file#kubernetes-configmap-secret-mounts).

```csharp
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules =>
    [
        rules.For<AppSettings>().FromIniFile("appsettings.ini"),
    ]));
```

## Format

```ini
; whole-line comments start with ; or #
# both are ignored

app = myapp                      ; keys before any [section] sit at the root

[Db]
Host = localhost
Port = 5432                      ; values are strings; the binder coerces (→ int)
Conn = Server=db;Database=app    ; ';' / '#' inside a value are kept

[Db.Primary]                     ; section names nest on '.' or ':'
Weight = 1
```

- `[Section]` headers and keys nest on `.` or `:` (e.g. `[Db.Primary]` → `{ "Db": { "Primary": { … } } }`), matching the [Environment Variables provider](/guide/providers/environment) convention.
- Surrounding single/double quotes are stripped.
- **Whole-line comments only** (a line starting with `;` or `#`). Inline comments are *not* stripped, so a value containing `;` or `#` — like a connection string — survives intact.
- Values are emitted as strings; the binder coerces them to the target type.

## Reactivity & per-tenant paths

Editing the file triggers a recompute. A config-aware overload resolves the path per recompute:

```csharp
rules.For<DbConfig>().FromIniFile(a => $"tenants/{a.Tenant}/db.ini").TenantScoped()
```

## Other formats

For `.toml` see the [TOML provider](/guide/providers/toml); `.yaml` / `.yml` → [YAML](/guide/providers/yaml); `.env` → [Dotenv](/guide/providers/dotenv).
