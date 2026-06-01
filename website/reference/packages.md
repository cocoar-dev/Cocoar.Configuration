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

::: tip
AspNetCore includes DI, which includes Core — you only need one `PackageReference`.
:::

### Cocoar.Configuration.Http

Remote configuration provider with support for one-time fetch, polling, and Server-Sent Events (SSE). Separate package to avoid forcing an HTTP dependency on all consumers.

- **Target:** .NET 9.0 / .NET 10.0
- **Dependencies:** Cocoar.Configuration
- **Key types:** `FromHttp()` extension method, `HttpRuleOptions`

```xml
<PackageReference Include="Cocoar.Configuration.Http" Version="6.*" />
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
| Remote config (polling / SSE) | Add `Cocoar.Configuration.Http` |
| Existing `IConfiguration` sources | Add `Cocoar.Configuration.MicrosoftAdapter` |

## External Dependencies

All shipped packages have **zero non-Microsoft external dependencies**. The only third-party packages are Cocoar ecosystem libraries (`Cocoar.Capabilities`, `Cocoar.FileSystem`, `Cocoar.Json.Mutable`).

`System.Reactive` is **not** a dependency — the library uses lightweight internal reactive primitives. Consumers are free to use System.Reactive on their side (the public API is `IObservable<T>`, which is BCL).

## License

All packages are licensed under **Apache-2.0**.
