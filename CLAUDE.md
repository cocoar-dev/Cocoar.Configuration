# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```powershell
# Build
dotnet build ./src -c Release

# Run all tests (excluding performance tests)
dotnet test ./src -c Release --filter "Type!=Performance"

# Run specific test project
dotnet test ./src/tests/Cocoar.Configuration.Core.Tests -c Release

# Run specific test by name pattern
dotnet test ./src -c Release --filter "FullyQualifiedName~TestMethodName"

# Run tests by trait (Unit, Performance, Concurrency, Stress)
dotnet test ./src -c Release --filter "Type=Unit"

# Run tests without rebuilding (faster iteration)
dotnet test ./src -c Release --no-build --filter "Type!=Performance"

# Pack NuGet packages
dotnet pack ./src -c Release
```

## Architecture Overview

**Cocoar.Configuration** is a reactive, strongly-typed configuration library for .NET 9.0. The architecture follows a modular, capabilities-driven design.

### Core Components

- **ConfigManager** (`src/Cocoar.Configuration/Core/`) - Central orchestrator that manages configuration lifecycle, rule execution, and reactive updates
- **Providers** (`src/Cocoar.Configuration/Providers/`) - Abstract configuration sources (File, Environment, CommandLine, HTTP, Static, Observable)
- **Fluent Builders** (`src/Cocoar.Configuration/Fluent/`) - `RulesBuilder` for defining configuration rules with `.For<T>().FromFile()` pattern
- **SetupBuilder** (`src/Cocoar.Configuration/Configure/`) - DI registration with `.ConcreteType<T>()` and `.Interface<T>()` patterns

### Recompute Pipeline

The configuration engine follows a transactional recompute model:

1. **Change Detection** - Provider signals change (file modified, HTTP poll, observable emission)
2. **Debouncing** - `RecomputeScheduler` coalesces rapid changes (default 300ms)
3. **Rule Execution** - Rules execute sequentially via `RuleManager` instances
4. **Atomic Commit** - `ConfigurationState` commits all changes atomically or rolls back entirely
5. **Reactive Notification** - `ReactiveConfigManager` emits to subscribers (hash-based change detection)

### RuleManager Coordination

Each rule has a `RuleManager` that coordinates:
- **RuleProviderLease** - Provider lifecycle (acquisition, key-based caching, disposal)
- **TransformCache** - Caches transformed bytes with hash-based invalidation
- **ChangeSubscription** - Manages provider change subscriptions

**Important ordering**: When providers change, the callback fires *before* rebuild to unsubscribe first, preventing spurious notifications from dispose events.

### Key Design Patterns

**Capabilities System** - Cross-assembly metadata composition without circular dependencies. Core defines builders; DI extends them via `Cocoar.Capabilities`:
```csharp
// Core: Defines primary capability
capabilityScope.Compose(this).WithPrimary(new ConcreteTypePrimary<T>(...));
// DI: Extends without coupling
SetupDefinition.GetComposer(builder).Add(new ServiceLifetimeCapability<T>(...));
```

**Reactive Tuples** - `IReactiveConfig<(T1, T2)>` provides atomic multi-config updates. Multiple configs always update together, preventing inconsistent state.

**Provider Consistency** - All providers return empty `{}` on failure (not null). Optional rules degrade gracefully with C# defaults; required rules roll back the entire recompute.

**Test Configuration** - `CocoarTestConfiguration` uses `AsyncLocal<T>` for test isolation. Supports `ReplaceConfiguration()` (skip originals), `AppendConfiguration()` (last-write-wins), and `ReplaceSecretsSetup()` (independent secrets override). Each concern is independent — chain freely on the returned `TestOverrideBuilder`.

### Project Structure

| Project | Purpose |
|---------|---------|
| `Cocoar.Configuration.Abstractions` | Lightweight interfaces (`IConfigurationAccessor`, `IReactiveConfig<T>`) |
| `Cocoar.Configuration` | Main library with providers, builders, and reactive engine |
| `Cocoar.Configuration.Secrets` | Memory-safe `Secret<T>` with RSA-OAEP + AES-256-GCM encryption |
| `Cocoar.Configuration.DI` | `AddCocoarConfiguration()` for Microsoft.Extensions.DI |
| `Cocoar.Configuration.AspNetCore` | ASP.NET Core integration and health endpoints |
| `Cocoar.Configuration.HttpPolling` | Remote config polling provider |
| `Cocoar.Configuration.MicrosoftAdapter` | Bridge to existing `IConfiguration` sources |
| `Cocoar.Configuration.Analyzers` | Roslyn analyzers (COCFG001-006) with quick fixes |

### DI Registration (Deterministic Ordering)

Service descriptors are emitted in deterministic order (sorted by type full name). The registration flow:
1. `ServiceRegistrationPlanner.CreatePlan()` - Builds plan from rules and setup definitions
2. `ServiceDescriptorEmitter.Emit()` - Applies plan to `IServiceCollection`

### DI Lifetimes

- **Scoped**: Concrete config types (stable snapshot per request)
- **Singleton**: `IReactiveConfig<T>` (continuous live updates)
- Customizable via `.AsSingleton()`, `.AsTransient()`

## Code Style

- **Comments explain *why*, not *what*** - Self-documenting code through clear naming is preferred
- **No commented-out code or debug statements**
- **Async patterns**: Use `Task`/`ValueTask`, no `async void`, `CancellationToken` as last parameter, `Async` suffix
- **Nullable reference types enabled** - Ensure annotations are accurate
- **Follow .NET conventions**: PascalCase, interfaces for inputs, concrete for outputs

## Key Architecture Decisions

Read these ADRs to understand important design choices:
- **ADR-001** (`docs/adr/`) - Capabilities system for cross-assembly extensibility
- **ADR-002** (`docs/adr/`) - Atomic reactive configuration updates (tuple semantics)
- **ADR-003** (`docs/adr/`) - Provider consistency (empty objects on failure)

## Documentation

- `/docs/` - Health monitoring, testing patterns, ADRs, migration guides
- `/docs/adr/` - Architecture Decision Records (ADR-001 through ADR-003)
- Project READMEs in `src/Cocoar.Configuration.Secrets/` and `src/Cocoar.Configuration.Analyzers/`
- Examples in `src/Examples/` demonstrate real-world usage patterns

## Local Working Files

The `.local/` folder (git-ignored) is for ephemeral work like release checklists and scratch notes. Never store secrets there or reference it from code/docs.
