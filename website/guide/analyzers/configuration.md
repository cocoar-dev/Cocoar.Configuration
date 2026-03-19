# Configuration Diagnostics

## COCFG001 — Secret Path Conflict {#cocfg001}

**Severity:** Warning

A non-secret property has the same path as a `Secret<T>` property, risking plaintext exposure.

```csharp
// ❌ Warning: ConnectionString conflicts with Secret<string> ConnectionString
rules.For<DbConfig>().FromFile("base.json"),
rules.For<DbConfig>().FromFile("local.json")
```

If `DbConfig.ConnectionString` is a `Secret<string>`, both rules target the same path — the second rule could overwrite the encrypted value with plaintext.

**Fix:** Use distinct paths or ensure both sources provide properly encrypted values.

## COCFG002 — Rule Dependency Ordering {#cocfg002}

**Severity:** Error

A rule depends on configuration that hasn't been loaded yet. Rules execute sequentially — if a rule uses `GetConfig<T>()` to read a type, that type's rule must appear earlier.

```csharp
// ❌ Error: DbConfig depends on AppConfig, but AppConfig isn't loaded yet
rules.For<DbConfig>().FromFile(cm => cm.GetConfig<AppConfig>()!.DbConfigPath),
rules.For<AppConfig>().FromFile("appsettings.json")
```

```csharp
// ✓ Fix: AppConfig first, then DbConfig can read it
rules.For<AppConfig>().FromFile("appsettings.json"),
rules.For<DbConfig>().FromFile(cm => cm.GetConfig<AppConfig>()!.DbConfigPath)
```

## COCFG003 — Required Rule Validation {#cocfg003}

**Severity:** Warning

A required rule references a file or resource that may not exist. If the resource is missing at startup, the application will fail to start.

```csharp
// ⚠️ Warning: Application won't start if missing.json doesn't exist
rules.For<AppSettings>().FromFile("missing.json").Required()
```

**Fix:** Verify the file exists in your deployment, or use `.Optional()` if the file is not critical.

## COCFG005 — Duplicate Unconditional Rules {#cocfg005}

**Severity:** Info

Multiple rules configure the same type without conditions. Since rules merge (last-write-wins), earlier unconditional rules are fully overwritten by later ones — wasting provider I/O.

```csharp
// ℹ️ Info: Second rule overwrites first entirely
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>().FromFile("appsettings.json")
```

```csharp
// ✓ Fix: Use conditions to differentiate
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>()
    .FromFile("appsettings.Production.json")
    .When(_ => IsProduction())
```

This diagnostic is informational — sometimes duplicates are intentional (e.g., base + environment overlay). Suppress it if the duplication is deliberate.

## COCFG006 — Static Provider Ordering {#cocfg006}

**Severity:** Info

A static rule appears after dynamic rules. Since rules merge property by property (later rules overlay earlier ones), a static rule at the end always wins — dynamic sources can never override it.

```csharp
// ℹ️ Info: Static rule after dynamic — static values always win
rules.For<AppSettings>().FromFile("appsettings.json"),
rules.For<AppSettings>().FromStatic(_ => new AppSettings { LogLevel = "Debug" })
```

```csharp
// ✓ Fix: Static defaults first, dynamic overrides after
rules.For<AppSettings>().FromStatic(_ => new AppSettings { LogLevel = "Info" }),
rules.For<AppSettings>().FromFile("appsettings.json")
```

The typical pattern is: static defaults first, then file/environment/HTTP sources that can override them.
