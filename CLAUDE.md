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

**Cocoar.Configuration** is a reactive, strongly-typed configuration library for .NET 9.0+ (multi-targets `net9.0` and `net10.0`). The architecture follows a modular, capabilities-driven design.

### Core Components

- **ConfigManager** (`src/Cocoar.Configuration/Core/`) - Central orchestrator that manages configuration lifecycle, rule execution, and reactive updates. Always created via `ConfigManager.Create()` or `ConfigManager.CreateAsync()` — constructors are internal. Both take an `Action<ConfigManagerBuilder>` lambda and return a fully initialized `ConfigManager`.
- **ConfigManagerBuilder** (`src/Cocoar.Configuration/Core/`) - Fluent builder received as parameter in `Create`/`CreateAsync` lambdas. Satellite libraries extend it via extension methods (e.g. `.UseSecretsSetup()`, `.UseFeatureFlags()`, `.UseEntitlements()`).
- **Feature Flags & Entitlements** (`src/Cocoar.Configuration/Flags/`) - Source-generated pattern: `partial class` implements `IFeatureFlags<TConfig>` or `IEntitlements<TConfig>`, generator produces constructor and `Config` property (reads `IReactiveConfig<T>.CurrentValue`). Multi-config via tuples (`IFeatureFlags<(T1, T2)>`). These interfaces are the only supported way to define flags and entitlements.
- **Providers** (`src/Cocoar.Configuration/Providers/`) - Abstract configuration sources (File, Environment, CommandLine, HTTP, Static, Observable, WritableStore)
- **Fluent Builders** (`src/Cocoar.Configuration/Fluent/`) - `RulesBuilder` for defining configuration rules with `.For<T>().FromFile()` pattern. `TypedRuleBuilder<T>` has a `where T : class` constraint — configuration types must be reference types.
- **SetupBuilder** (`src/Cocoar.Configuration/Configure/`) - DI registration with `.ConcreteType<T>()` and `.Interface<T>()` patterns
- **Multi-Tenancy** (`src/Cocoar.Configuration/Core/TenantPipeline.cs`) - One `ConfigManager` owns a global `TenantPipeline` plus a per-tenant registry on a shared global base. Author one flat rule list with `.TenantScoped()`; `Tenant` on `IConfigurationAccessor`; consume via `…ForTenant(id)` on `ITenantConfigurationAccessor` (explicit, never DI-injected). See ADR-005.
- **Service-Backed (DI-aware) Configuration** (`src/Cocoar.Configuration.DI/ServiceBacked/`) - Two-layer model: eager no-DI `UseConfiguration` (Layer 1) + lazy `UseServiceBackedConfiguration` (Layer 2) whose provider factories receive `IServiceProvider` (so providers can use `IHttpClientFactory`/Marten/EF). Activated on host start via `IHostedLifecycleService` (a recompute, never a rebuild). No-DI core preserved. See ADR-006.

### Recompute Pipeline

The configuration engine follows a transactional recompute model:

1. **Change Detection** - Provider signals change (file modified, HTTP poll, observable emission)
2. **Debouncing** - `RecomputeScheduler` coalesces rapid changes (default 300ms)
3. **Async Dispatch** - `ScheduleAsync` dispatches a fully async recompute; the threadpool thread is released during provider I/O (no sync-over-async)
4. **Rule Execution** - Rules execute sequentially via `RuleManager` instances
5. **Atomic Commit** - `ConfigurationState` commits all changes atomically or rolls back entirely
6. **Reactive Notification** - `ReactiveConfigManager` emits to subscribers (reference-equality change detection)

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

**Zero External Dependencies** - Core shipped packages have no non-Microsoft dependencies. (Opt-in integration packages are the deliberate exception: `Cocoar.Configuration.Marten` takes a Marten dependency. Consumers who don't reference it pay nothing.) The reactive internals (`Reactive/Internal/`) are lightweight replacements for the subset of System.Reactive the library used (Subject, BehaviorSubject, Select/Where/DistinctUntilChanged). This is intentional — do not add System.Reactive back. The public API (`IReactiveConfig<T> : IObservable<T>`) uses only BCL types; consumers are free to use System.Reactive on their side. Test projects still reference System.Reactive as a test dependency.

**Reactive Tuples** - `IReactiveConfig<(T1, T2)>` provides atomic multi-config updates. Multiple configs always update together, preventing inconsistent state.

**Provider Consistency** - All providers return empty `{}` on failure (not null). Optional rules degrade gracefully with C# defaults; required rules roll back the entire recompute.

**Test Configuration** - `CocoarTestConfiguration` uses `AsyncLocal<T>` for test isolation. Supports `ReplaceConfiguration()` (skip originals), `AppendConfiguration()` (last-write-wins), and `ReplaceSecretsSetup()` (independent secrets override). Each concern is independent — chain freely on the returned `TestOverrideBuilder`.

### Project Structure

| Project | Purpose |
|---------|---------|
| `Cocoar.Configuration.Abstractions` | Lightweight interfaces (`IConfigurationAccessor`, `IReactiveConfig<T>`, `ISecret<T>`, `SecretLease<T>`) |
| `Cocoar.Configuration` | Main library: providers (incl. the writable store overlay), builders, reactive engine, multi-tenancy, secrets (`Secret<T>`, X.509 encryption), feature flags, entitlements |
| `Cocoar.Configuration.DI` | `AddCocoarConfiguration()` for Microsoft.Extensions.DI (no ASP.NET Core dependency); service-backed (Layer-2) configuration via `UseServiceBackedConfiguration` |
| `Cocoar.Configuration.AspNetCore` | ASP.NET Core integration, health endpoints, feature flag/entitlement REST endpoints (incl. per-tenant), secrets encryption-key endpoints, scoped tenant config adapter |
| `Cocoar.Configuration.Http` | Remote config provider (polling, SSE, one-time fetch) |
| `Cocoar.Configuration.MicrosoftAdapter` | Bridge to existing `IConfiguration` sources |
| `Cocoar.Configuration.Marten` | Marten (PostgreSQL) WritableStore backend (`MartenStoreBackend`, `FromMartenStore()`); tenant-aware via Marten database-per-tenant. Opt-in integration package that takes a Marten dependency. |
| `Cocoar.Configuration.Analyzers` | Roslyn analyzers (COCFG001, 002, 003, 005, 006) and source generator (COCFLAG001-003). COCFG004 was removed — enforced by `where T : class` constraint instead. |
| `Cocoar.Configuration.Secrets.Cli` | Global .NET tool for encrypting/decrypting secrets in config files |

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
- **ADR-001** (`website/adr/`) - Capabilities system for cross-assembly extensibility
- **ADR-002** (`website/adr/`) - Atomic reactive configuration updates (tuple semantics)
- **ADR-003** (`website/adr/`) - Provider consistency (empty objects on failure)
- **ADR-004** (`website/adr/`) - Aggregate rules with isolated execution boundary
- **ADR-005** (`website/adr/`) - Multi-tenant configuration (per-tenant pipelines on a shared global base)
- **ADR-006** (`website/adr/`) - DI-aware (service-backed) two-layer configuration

## Documentation

- `website/` - VitePress documentation site (single source of truth for user-facing docs)
- `website/adr/` - Architecture Decision Records (ADR-001 through ADR-006), published in the docs site under the **ADR** top-nav
- `src/Examples/` - Runnable example projects demonstrating individual features

## Local Working Files

The `.local/` folder (git-ignored) is for ephemeral work like release checklists and scratch notes. Never store secrets there or reference it from code/docs.
