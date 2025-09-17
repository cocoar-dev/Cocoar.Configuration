# Cocoar.Configuration Deep Scenarios (Draft)

> Extended guidance & rationale for patterns that go beyond the quick start.

**Related Docs:** [Architecture](ARCHITECTURE.md) · [Advanced Features](ADVANCED.md) · [Concepts](CONCEPTS.md) · [Providers](PROVIDERS.md) · [Migration](MIGRATION.md) · [Provider Dev](PROVIDER_DEV.md)

## 1. Philosophy Beyond the Quick Start
Why layering + atomic snapshots + reactive channels coexist:
- **Layered rules** express intent & ordering explicitly
- **Atomic recompute** prevents consumers from seeing half‑merged state
- **Reactive stream (`IReactiveConfig<T>`)** decouples push semantics from DI lifetimes

## 2. DI & Registration Patterns
Matrix (auto-registration = AR):
| Artifact | Default Lifetime | Auto Registered | Notes |
|----------|------------------|-----------------|-------|
| Concrete rule type (e.g. `AppSettings`) | Scoped | Yes | Lifetime configurable via `DefaultRegistrationLifetime` |
| Bound interface (e.g. `IAppSettings`) | Scoped | Yes if bound | Same underlying snapshot instance |
| `IReactiveConfig<T>` | Singleton | Always (unless disabled) | Hash‑gated emissions |
| `ConfigManager` | Singleton | Always | Core access surface |

Key variations:
```csharp
// Disable all auto-registration; go manual
options.DefaultRegistrationLifetime(null);

// Override one service
options.Register
    .Remove<AppSettings>()
    .Add<AppSettings>(ServiceLifetime.Singleton);

// Add keyed variants
options.Register
    .Add<IDatabaseConfig>(ServiceLifetime.Singleton, "primary")
    .Add<IDatabaseConfig>(ServiceLifetime.Scoped, "shadow");
```

## 3. Consumption Patterns
### Web / HTTP Endpoint
Inject concrete types → stable per-request snapshot.

### Background Service
Prefer `IReactiveConfig<T>`:
```csharp
public sealed class FeatureLoop(IReactiveConfig<AppSettings> live) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var sub = live.Subscribe(cfg => Console.WriteLine($"Flag={cfg.FeatureFlag}"));
        while (!token.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(30), token);
    }
}
```
Need a frozen baseline? Capture once:
```csharp
var baseline = live.CurrentValue; // store once
```
Rarely required—prefer live.

### Library Code (No DI Assumption)
Accept `IConfigurationAccessor` (or `ConfigManager`) and request what you need: `accessor.GetRequiredConfig<MySettings>()`.

## 4. Dynamic Rule Factories
Later rule can read earlier snapshots safely:
```csharp
Rule.From.File("meta.json").For<RemoteMeta>().Required();
Rule.From.HttpPolling(cm => new HttpPollingRuleOptions(
    urlPathOrAbsolute: cm.GetRequiredConfig<RemoteMeta>().Url,
    baseAddress: "https://api.example.com",
    pollInterval: TimeSpan.FromSeconds(30)))
    .For<RemotePayload>();
```
Guidelines:
- Only read earlier-produced types
- Use `GetConfig<T>()` if optional; `GetRequiredConfig<T>()` enforces ordering
- Combine with `.UseWhen(() => condition)` for conditional remote fetch
Anti‑patterns: mutually dependent rules, using dynamic factories for trivial constants.

## 5. Recompute Mechanics (Practical View)
Trigger sources: file watcher, HTTP polling emission, other provider change.
Process:
1. Identify earliest changed rule index
2. Cancel any in-flight later recompute
3. Re-run from that index forward
4. Publish new snapshot atomically
5. Notify reactive observers (hash-gated per type)

Debounce: collapses rapid consecutive change bursts.

Selection hashing: if selected subtree unchanged → skip recompute gating for downstream.

## 6. Performance Tuning
| Lever | Benefit | Notes |
|-------|---------|-------|
| Reduce rule count | Less flatten/merge cost | Combine related JSON when stable |
| Streaming hash (built-in) | Allocation reduction | Already default | 
| Increase poll interval | Lower remote churn | Balance freshness vs cost |
| Singleton large immutable config | Fewer allocations | Only if truly global |
| Avoid oversized transient rules | GC pressure | Keep rules coarse-grained |

MD5 rationale: fastest practical 128-bit hash sufficient for collision-resilient change detection in config domain.

## 7. Troubleshooting
| Symptom | Likely Cause | Action |
|---------|--------------|--------|
| Config not updating in handler | Using scoped snapshot mid-request | Use `IReactiveConfig<T>` if true live needed |
| Interface resolution null | Missing binding | Add `Bind.Type<Concrete>().To<IInterface>()` |
| Duplicate reactive emissions | (Shouldn’t occur) | Check custom provider producing semantic duplicates |
| Rule never fires | Wrong file path / env prefix | Verify absolute path & prefix normalization |
| Required rule exception | Source missing / deserialize fail | Fix source or mark rule optional |

## 8. Migration & Interop
- Gradual replacement of `IOptions<T>`: inject both until consumers move
- Microsoft adapter: bridge existing `IConfigurationSource` assets
- Keep parallel layering for phased adoption

## 9. Security Patterns
Pattern layering: static defaults → environment → remote secret store (adapter or HTTP) → ephemeral overrides.
Never bake secrets into versioned JSON.

## 10. FAQ (Concise)
**Q:** Why MD5 not SHA256?  
**A:** Faster; collision risk negligible for small structured config payloads; used only for change gating.

**Q:** Can a recompute race drop updates?  
**A:** No—earliest-index restart strategy + atomic publish ensures latest wins deterministically.

**Q:** How to force recompute manually?  
**A:** Touch a watched provider (e.g. rewrite file) or implement a custom provider emitting a change.

**Q:** Can I diff snapshots?  
**A:** Export two configs as JSON (future helper planned). For now, compute your own flattened view via reflection.

---
*Draft – sections may expand with examples & diagrams.*
