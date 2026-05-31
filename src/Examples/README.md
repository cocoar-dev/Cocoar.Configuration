# Examples

This directory contains runnable examples for **Cocoar.Configuration**. Each subfolder is an independent project you can open, run, and modify.

## Projects

### Core & providers
- **SimplifiedCoreExample** – Pure core library usage without DI (`ConfigManager` only)
- **BasicUsage** – Common ASP.NET Core pattern with file + environment overrides
- **FileLayering** – Multiple JSON file layering (base + env + local)
- **StaticProviderExample** – Static seeding with JSON strings and factory functions
- **CommandLineExample** – Command-line argument provider with configurable prefixes
- **HttpPollingExample** – Remote/polling configuration pattern
- **MicrosoftAdapterExample** – Bridging existing `IConfiguration`/`IConfigurationSource` providers
- **GenericProviderAPI** – Using the generic provider registration API

### Rules, dependencies & reactivity
- **ConditionalRulesExample** – `When()` with config-aware predicates for conditional rule execution
- **DynamicDependencies** – Later rules derive values from earlier configurations
- **AggregateRules** – Composable rule grouping (`FromFiles` sugar) with byte-level JSON merge
- **TupleReactiveExample** – Tuple-based reactive multi-config snapshot & aligned emission demo

### Multi-tenancy
- **MultiTenancyExample** – The same type resolves to a different value per tenant: one flat rule list with a global base + a `.TenantScoped()` overlay whose file path is interpolated from `accessor.Tenant`; sparse per-tenant inheritance

### DI & ASP.NET Core
- **ExposeExample** – Interface exposure without DI frameworks
- **AspNetCoreExample** – Minimal API exposing configuration via endpoints
- **ServiceBackedConfig** – DI-aware service-backed configuration (ADR-006): eager Layer-1 + lazy `IServiceProvider`-gated Layer-2 that activates on host start via a recompute
- **TestingOverridesExample** – Test isolation with `CocoarTestConfiguration` (`Replace`/`Append` overrides)

### Writable store & secrets
- **WritableStoreExample** – Writable sparse-overlay store (`FromStore`): set/reset/clear plus `DescribeAsync` provenance
- **SecretsBasicExample** – Basic `Secret<T>` usage with a self-signed certificate
- **SecretsCertificateExample** – Production-ready secrets with certificate-from-file decryption of pre-encrypted values

## Running an Example
```pwsh
cd src/Examples/BasicUsage
dotnet run
```

Some examples expect local JSON like `config.json`, `base.json`, etc. Copy the inline examples from the source `Program.cs` comments.

## Switching to PackageReference
Currently examples use `ProjectReference` to always reflect the latest API. To test against a published package:
1. Remove the `<ProjectReference/>` entries
2. Add:
```xml
<ItemGroup>
  <PackageReference Include="Cocoar.Configuration" Version="1.0.0" />
</ItemGroup>
```
(And any adapter/provider packages you need.)

## Building
Examples are excluded from packing. The build is directory-based — there is no solution file. Build them along with the rest of the source tree:
```pwsh
dotnet build ./src -c Release
```

## Adding a New Example
1. Create `src/Examples/<Name>/<Name>.csproj` (copy one of the existing ones)
2. Add `Program.cs` with top-level statements (or a `Main`)
3. Add it to the list above

The directory-based build picks up the new project automatically — no solution file to update.

Feel free to keep examples minimal—prefer a single `Program.cs` unless the scenario requires more.
