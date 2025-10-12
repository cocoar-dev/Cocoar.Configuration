# Examples

This directory contains runnable examples for **Cocoar.Configuration**. Each subfolder is an independent project you can open, run, and modify.

## Projects

- **BasicUsage** – Common ASP.NET Core pattern with file + environment overrides
- **FileLayering** – Multiple JSON file layering (base + env + local)  
- **DynamicDependencies** – Later rules derive values from earlier configurations
- **ConditionalRulesExample** – Using `When()` with config-aware predicates for conditional rule execution
- **AspNetCoreExample** – Minimal API exposing configuration via endpoints
- **GenericProviderAPI** – Using the generic provider registration API
- **HttpPollingExample** – Demonstrates pattern for remote/polling configuration
- **MicrosoftAdapterExample** – Integrating `IConfigurationSource` providers
- **StaticProviderExample** – Static seeding with JSON strings and factory functions
- **SimplifiedCoreExample** – Pure core library usage without DI (ConfigManager only)
- **ExposeExample** – Interface exposure without DI frameworks
- **TupleReactiveExample** – Tuple-based reactive multi-config snapshot & aligned emission demo

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

## CI / Build
Examples are excluded from packing. Build manually if needed:
```pwsh
dotnet build src/Examples/Examples.sln -c Release
```

## Adding a New Example
1. Create `src/Examples/<Name>/<Name>.csproj` (copy one of the existing ones)
2. Add `Program.cs` with top-level statements
3. Add the project to `Examples.sln` (or let someone run `dotnet sln add` later)

Feel free to keep examples minimal—prefer a single `Program.cs` unless scenario requires more.
