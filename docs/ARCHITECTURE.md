# Architecture

## Execution & Merge Pipeline

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

## Required vs Optional

* Required: failures block recompute for that type.
* Optional: failures tolerated and skipped.
