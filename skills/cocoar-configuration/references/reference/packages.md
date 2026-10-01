<!-- Generated from website/reference/packages.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# Package Overview

## Packages

### Cocoar.Configuration.Abstractions

Lightweight interfaces for decoupled architecture. Reference this from libraries that need to accept configuration without depending on the full implementation.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** None
- **Key types:** `IConfigurationAccessor`, `IReactiveConfig<T>`, `ISecret<T>`, `SecretLease<T>`

```xml
<PackageReference Include="Cocoar.Configuration.Abstractions" Version="6.*" />
```

### Cocoar.Configuration

The core library. Includes providers, reactive engine, secrets, feature flags, and entitlements.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration.Abstractions, Cocoar.Configuration.Analyzers (build-time), Cocoar.Capabilities, Cocoar.FileSystem, Cocoar.Json.Mutable, Microsoft.Extensions.Logging.Abstractions
- **Key types:** `ConfigManager`, `Secret<T>`, `IFeatureFlags<TConfig>`, `IEntitlements<TConfig>`, `FeatureFlag<T>`, `Entitlement<T>`

```xml
<PackageReference Include="Cocoar.Configuration" Version="6.*" />
```

### Cocoar.Configuration.DI

Microsoft.Extensions.DependencyInjection integration.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration, Cocoar.Capabilities
- **Key types:** `AddCocoarConfiguration()` extension method

```xml
<PackageReference Include="Cocoar.Configuration.DI" Version="6.*" />
```

### Cocoar.Configuration.AspNetCore

ASP.NET Core integration — includes DI and adds health checks, feature flag/entitlement REST endpoints.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration, Cocoar.Configuration.DI, Microsoft.AspNetCore.App (FrameworkReference)
- **Key types:** `AddCocoarConfigurationHealthCheck()`, `MapFeatureFlagEndpoints()`, `MapEntitlementEndpoints()`

```xml
<PackageReference Include="Cocoar.Configuration.AspNetCore" Version="6.*" />
```

> **Tip**
>
> AspNetCore includes DI, which includes Core — you only need one `PackageReference`.

### Cocoar.Configuration.Http

Remote configuration provider with support for one-time fetch, polling, and Server-Sent Events (SSE). Separate package to avoid forcing an HTTP dependency on all consumers.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration
- **Key types:** `FromHttp()` extension method, `HttpRuleOptions`

```xml
<PackageReference Include="Cocoar.Configuration.Http" Version="6.*" />
```

### Cocoar.Configuration.ConfigHub

ConfigHub-specific delivery provider. It loads authenticated, authoritative JSON snapshots and uses Server-Sent Events only to invalidate the current snapshot. ETag validation, reconnect reconciliation, and optional polling fallback keep instances current without trusting event payloads as configuration.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration
- **Key types:** `FromConfigHub()` extension method, `ConfigHubRuleOptions`

```xml
<PackageReference Include="Cocoar.Configuration.ConfigHub" Version="6.*" />
```

### Cocoar.Configuration.MicrosoftAdapter

Bridge from `Microsoft.Extensions.Configuration` sources (Azure Key Vault, custom providers, etc.) into Cocoar.Configuration.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration, Microsoft.Extensions.Configuration.*
- **Key types:** `FromIConfiguration()` extension method

```xml
<PackageReference Include="Cocoar.Configuration.MicrosoftAdapter" Version="6.*" />
```

### Cocoar.Configuration.WritableStore.Marten

Marten (PostgreSQL document store) backend for the WritableStore. Persists writable configuration overlays as documents, with first-class support for Marten database-per-tenant multi-tenancy so each tenant's configuration lives in its own database. Opt-in package — it intentionally takes a Marten dependency; consumers who don't reference it pay nothing.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration.DI, Marten
- **Key types:** `MartenStoreBackend`, `CocoarConfigDocument`, `FromMartenStore()` extension method

```xml
<PackageReference Include="Cocoar.Configuration.WritableStore.Marten" Version="6.*" />
```

### Cocoar.Configuration.Yaml

YAML file provider. Reads `.yaml`/`.yml` files into the configuration pipeline with reactive file-watching. Plain YAML scalars are mapped to their JSON types (booleans, numbers, null) so they bind like JSON; quoted and block scalars stay strings. Opt-in package — it takes a YamlDotNet dependency. (The `.env` / dotenv provider, `FromDotEnv()`, is built into the core package and needs no dependency.)

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration, YamlDotNet
- **Key types:** `YamlFileProvider`, `FromYamlFile()` extension method

```xml
<PackageReference Include="Cocoar.Configuration.Yaml" Version="6.*" />
```

### Cocoar.Configuration.Toml

TOML file provider. Reads `.toml` files into the configuration pipeline with reactive file-watching. TOML's typed values (strings, integers, floats, booleans, dates, arrays, tables, arrays-of-tables) map unambiguously to JSON so they bind like JSON. Opt-in package — it takes a Tomlyn dependency. (The `.ini` and `.env` providers are built into the core package and need no dependency.)

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration, Tomlyn
- **Key types:** `TomlFileProvider`, `FromTomlFile()` extension method

```xml
<PackageReference Include="Cocoar.Configuration.Toml" Version="6.*" />
```

### Cocoar.Configuration.Analyzers

Roslyn analyzers (COCFG001–006) and source generator (COCFLAG001–003). Ships as a build-time dependency of the core package — you don't need to install it separately.

- **Target:** .NET Standard 2.0 (Roslyn requirement)
- **Dependencies:** Microsoft.CodeAnalysis.CSharp (build-time only)
- **Key types:** 5 configuration analyzers, 3 flags diagnostics, 1 incremental source generator

### Cocoar.Configuration.Secrets.Cli

Global .NET tool for encrypting and decrypting secrets in JSON configuration files.

```shell
dotnet tool install -g Cocoar.Configuration.Secrets.Cli
```

- **Target:** .NET 9.0
- **Commands:** `encrypt`, `decrypt`, `generate-cert`, `convert-cert`, `cert-info`

## Dependency Graph

```
Abstractions (no deps)
    │
    ▼
  Core ◄──── Analyzers (build-time)
    │
    ├──► ConfigHub
    ├──► Http
    ├──► MicrosoftAdapter
    │
    ▼
   DI
    │
    ▼
 AspNetCore
```

Each arrow means "depends on". Installing a downstream package brings all upstream packages transitively.

## Which Package Do I Need?

| Scenario | Package |
|---|---|
| ASP.NET Core application | `Cocoar.Configuration.AspNetCore` |
| Console app or library with DI | `Cocoar.Configuration.DI` |
| Library without DI | `Cocoar.Configuration` |
| Interface-only dependency | `Cocoar.Configuration.Abstractions` |
| ConfigHub-managed configuration | Add `Cocoar.Configuration.ConfigHub` |
| Remote config (polling / SSE) | Add `Cocoar.Configuration.Http` |
| Existing `IConfiguration` sources | Add `Cocoar.Configuration.MicrosoftAdapter` |

## External Dependencies

Dependencies stay local to the package that needs them. `Cocoar.Configuration.ConfigHub` and `Cocoar.Configuration.Http` add no dependency beyond the core package and the .NET BCL. Format and persistence integrations intentionally bring their documented libraries, such as YamlDotNet, Tomlyn, and Marten; the core uses Cocoar ecosystem libraries and Microsoft abstractions.

`System.Reactive` is **not** a dependency — the library uses lightweight internal reactive primitives. Consumers are free to use System.Reactive on their side (the public API is `IObservable<T>`, which is BCL).

## Agent Skill

`Cocoar.Configuration` ships an [Agent Skill](https://agentskills.io/) — a `SKILL.md` plus this documentation page by page as reference files — so a coding assistant in your project knows the library's API and the mistakes it would otherwise make. It sits in a `skills/` folder at the package root and takes no part in your build or your IDE: nothing is copied or shown anywhere unless you install it.

With [agentskills-cli](https://mysticmind.github.io/agentskills-cli/):

```shell
dotnet tool install --global agentskills-cli
agentskills-cli add Cocoar.Configuration
```

That places the skill in `.claude/skills/` for Claude Code and `.agents/skills/` for Cursor, Codex, Copilot and other agents that read the standard; `-g` installs it globally instead. Without the tool, copy `skills/cocoar-configuration/` out of the package into the same folders by hand.

The skill is generated from these docs, so it says what the docs say for the version you reference. The same content is available online as [llms.txt](https://docs.cocoar.dev/configuration/llms.txt) (an index with one line per page) and [llms-full.txt](https://docs.cocoar.dev/configuration/llms-full.txt) (everything in one file) for assistants that fetch documentation instead of reading local files.

## License

All packages are licensed under **Apache-2.0**.
