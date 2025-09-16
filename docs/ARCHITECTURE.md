# Architecture

## System Overview

Cocoar.Configuration consists of three main architectural layers:

1. **Core Library**: Rule-based configuration merging and snapshot management
2. **Binding System**: Interface-to-concrete type mapping for clean separation  
3. **DI Integration Package**: Complete dependency injection integration with auto-registration

---

## Core Library: Execution & Merge Pipeline

* Each rule targets one config type and queries exactly one provider.
* Recompute builds ordered list of layers, flattens to colon keys, applies last-write-wins, then materializes snapshot.
* Dynamic factories may read snapshots earlier in recompute.
* Arrays are replaced whole.

---

## Change & Recompute Model

### Overview

Change handling is incremental, cancellation-aware, and coalesced. The system minimizes work while preserving a deterministic, atomic publish of the merged snapshot.

### Pipeline Recap

Fetch → Select → Mount → Flatten → Merge → Materialize Snapshot → Publish (atomic swap)

### Components

* Change Subscription Manager: wires provider change events into a single coalescer.
* Recompute Coalescer: aggregates earliest changed rule index, applies immediate scheduling plus short trailing debounce, and starts recomputes with cancellation.
* Configuration Orchestrator: performs partial recompute from earliest changed rule onward.
* Rule Manager: stores per-rule last selection hash and last flattened contribution map.

### Earliest Changed Rule Detection

1. Providers signal a change for their bound rule index.
2. The coalescer atomically records `earliestIndex = min(existing, changedIndex)`.
3. A recompute pass starts (immediately) if none running; otherwise the running pass may be cancelled if a new earlier index arrives.

### Selection Hash Gating (No-Op Suppression)

* After provider fetch + selection, a stable hash (e.g., FNV-1a of normalized JSON) is computed.
* If unchanged from the previous hash for that rule, the rule is treated as unchanged for earliest-index purposes (unless no snapshot exists yet).
* This prevents spurious recomputes when the unselected parts of the provider output change or a provider re-emits identical data.

### Partial Recompute Algorithm

1. Determine earliest changed index (ECI). All rules `< ECI` reuse their previously stored flattened contributions (prefix reuse).
2. Starting at ECI, recompute each rule in order:
	* Fetch provider (reuse instance unless factory options changed).
	* Apply selection (.Select path) before mounting.
	* Flatten contribution (colon-delimited keys) and compare with prior flattened map to detect deletions.
	* Update stored flattened map and selection hash.
3. Merge: Build merged dictionary by copying reused prefix contributions then overlay newly recomputed suffix contributions (ordered last-write-wins per key).
4. Apply deletions: If a key existed in the prior flattened map for a recomputed rule but not in the new one, remove it from the merged result (unless overwritten later in the suffix).
5. Materialize strongly-typed objects for each registered config type from the merged flattened snapshot.
6. Atomically publish (swap references) so all consumers see a consistent point-in-time view.

### Cancellation

* If a new change arrives targeting an earlier rule while a pass is in progress, a cancellation token is triggered.
* The orchestrator aborts work promptly between rule boundaries and restarts from the new (smaller) earliest index.

### Debounce & Coalescing Strategy

* Immediate schedule: first change starts a pass without waiting.
* Trailing debounce: short (25–50 ms) timer collapses rapid bursts, allowing multiple change signals to converge before the next pass.
* Only one recompute runs at a time; overlapping events update earliest index and request cancellation.

### Provider & Rule Reuse

* Providers are reused unless rule factory inputs change (ensures stable watching/polling resources).
* Rule set is static post-initialization; use `UseWhen` to conditionally contribute without structural mutation.

### Data Stored Per Rule

* Last selection hash (string)
* Last flattened contribution (Dictionary<string,string>)

### Guarantees

* Deterministic ordering and merge semantics.
* Atomic snapshot visibility (readers never observe mid-merge state).
* Minimal recompute scope: unchanged prefix never refetched or re-flattened.
* No-op provider emissions suppressed by selection hash gating.
* Deletions propagate correctly when selected subtree shrinks.

### Future / Deferred Enhancements

* Stress / load benchmarks for high-frequency change storms.
* Optional pluggable hash algorithm or structural diff.
* Metrics hooks (pass duration, skipped rules count, gating hit rate).

---

## Binding System Architecture

### Purpose
The binding system provides clean separation between concrete configuration types and the interfaces they implement, enabling dependency injection without coupling the core library to DI frameworks.

### Components

**BindingSpec**: Immutable record defining a concrete type and its bound interfaces:
```csharp
public record BindingSpec(Type ConcreteType, IReadOnlySet<Type> BoundInterfaces);
```

**BindingRegistry**: Thread-safe registry managing interface → concrete type mappings with conflict detection:
- **Registration**: Maps each interface to exactly one concrete type
- **Conflict Detection**: Prevents multiple concrete types binding to same interface  
- **Resolution**: Fast lookup from interface to concrete type during `GetConfig<T>()`

**Fluent API**: Clean, discoverable binding creation:
```csharp
Bind.Type<PaymentConfig>().To<IPaymentConfig>().To<IReadOnlyConfig>()
```

### Resolution Process

1. **Direct Lookup**: `GetConfig<ConcreteType>()` → direct snapshot access
2. **Interface Binding Resolution**: `GetConfig<Interface>()` → binding registry lookup → concrete type → snapshot access → cast to interface
3. **Validation**: Runtime verification that concrete type implements requested interface
4. **Caching**: Fast path for repeated interface resolutions

### Integration Points

- **ConfigManager**: Accepts optional binding collection in constructor
- **DI Package**: Uses bindings to automatically register interface services  
- **Examples**: BindingExample demonstrates usage without DI, DIExample shows full DI integration

---

## DI Integration Package Architecture

### Design Philosophy
- **Zero-Config**: Works perfectly with just rules - no additional setup required
- **Progressive Enhancement**: Add bindings and options only when needed  
- **Fail-Safe**: Impossible to forget method calls or cause runtime surprises
- **Standard Patterns**: Follows ASP.NET Core DI conventions

### Core Components

**CocoarConfigurationExtensions**: Main entry points with multiple overloads:
- `AddCocoarConfiguration(rules)` - Simplest case, auto-registration only
- `AddCocoarConfiguration(rules, bindings)` - Add interface bindings  
- `AddCocoarConfiguration(rules, bindings, options)` - Full control with lifetime management
- `AddCocoarConfiguration(configManager, options)` - Pre-built ConfigManager support

**ServiceRegistrationOptions**: Configuration for auto-registration behavior:
- **DefaultRegistrationLifetime**: Controls automatic registration (default: Scoped, null to disable)
- **ServiceRegistrationBuilder**: Fluent Add/Remove API for fine-grained control

**ServiceRegistration**: Type-safe service definition:
```csharp  
public record ServiceRegistration(Type ServiceType, ServiceLifetime Lifetime, object? ServiceKey = null);
```

### Auto-Registration Algorithm

1. **Rule Types**: All concrete types from rules automatically registered with default lifetime
2. **Binding Interfaces**: All interfaces from bindings automatically registered with default lifetime  
3. **Explicit Overrides**: `options.Register.Add<T>()` registrations processed after auto-registration
4. **Removals**: `options.Register.Remove<T>()` prevents auto-registration
5. **ConfigManager**: Always registered as singleton for manual access

### Service Resolution Flow

```
User Requests IPaymentConfig
    ↓
DI Container Lookup
    ↓  
Service Factory: serviceProvider.GetRequiredService<ConfigManager>()
    ↓
ConfigManager.GetConfig<IPaymentConfig>()
    ↓
Binding Resolution: IPaymentConfig → PaymentConfig  
    ↓
Snapshot Access + Cast
    ↓
Return IPaymentConfig Instance
```

### Lifetime Management

- **Default**: Scoped (sensible for web applications, safe for console apps)
- **Override**: `options.DefaultRegistrationLifetime(ServiceLifetime.Singleton)`  
- **Disable**: `options.DefaultRegistrationLifetime(null)` - no auto-registration
- **Per-Type**: `options.Register.Add<T>(ServiceLifetime.Transient)`
- **Keyed Services**: `options.Register.Add<T>(ServiceLifetime.Scoped, "backup")`

---

## Required vs Optional

* Required: failures block recompute for that type.
* Optional: failures tolerated and skipped.

---

## Correctness Guarantees

The incremental engine optimizes work (prefix reuse, selection-hash gating, cancellation, debounce) but MUST preserve the exact end-state a full serial recompute would have produced.

### Invariants After Quiescence

1. Last-write-wins ordering: For the static rule sequence R0..Rn the published snapshot equals applying each participating rule in order and overwriting flattened keys.
2. No regression: A key cannot revert to an older value once overwritten by a later rule unless a subsequent change reintroduces that older value via a new provider emission.
3. Proper deletions: Keys removed by a recomputed rule disappear unless re-added later in the sequence.
4. Atomicity: Consumers never observe a partially merged intermediate state; only fully merged snapshots are published.
5. Selection isolation: Changes outside a rule's selected subtree never force recompute nor alter snapshot state.

### Test Suites Enforcing These

* DifferentialCorrectnessFuzzTests: Random mutation waves; compares final published flattened map to a naive full recompute (independent merge) ensuring bit-level equality.
* OverlappingRecomputeCorrectnessTests: Descending index storms maximize cancellation; verifies each provider's latest versioned contribution is reflected (no lost updates).
* Existing partial recompute & deletion tests: Confirm earliest-index detection + deletion propagation semantics.

### Removed Timing Heuristics

Early load tests asserted wall-clock thresholds and per-provider refetch counts; these were dropped because timing variance is environmental and not a correctness dimension. The differential comparison provides a stronger, deterministic guarantee.

### Planned Extensions

* Rule-order permutation differential runs (shuffle static ordering per test run) to demonstrate correct precedence handling independent of concurrency.
* Optional debug-only invariant: compute naive merge post-publish and assert equality (disabled in production for cost reasons).

These layers ensure optimisation cannot silently compromise correctness.

---

## Testing Strategy (Overview)

The correctness guarantees above are enforced by a layered test taxonomy:

| Layer | Purpose | Representative Suites |
|-------|---------|-----------------------|
| Differential end-state | Prove incremental == full naive merge | `DifferentialCorrectnessFuzzTests` |
| Incrementality rules | Earliest-index, prefix replay, suffix recompute | `PartialRecomputeTests`, `OverlappingRecomputeCorrectnessTests` |
| Cancellation & coalescing | Ensure no lost updates under restart storms | `CancellationTests`, `OverlappingRecomputeCorrectnessTests` |
| Deletion semantics | Removal propagation & non-resurrection | `SnapshotChangeDeletionTests` |
| Stress & burst behaviour | Stability under high-frequency change storms | `RecomputeStressTests` |
| Provider integration | Source-specific behaviors (file, env, HTTP, adapter) | `Providers/*` suites |

Each suite asserts functional invariants, not timing heuristics. Fuzz differential tests are the final safety net ensuring every optimisation preserves the canonical merge result.

For an at-a-glance summary see the "Quality & Reliability" section in the top-level `README.md`.
