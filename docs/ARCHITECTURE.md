# Architecture

## Execution & Merge Pipeline

* Each rule targets one config type and queries exactly one provider.
* Recompute builds ordered list of layers, flattens to colon keys, applies last-write-wins, then materializes snapshot.
* Dynamic factories may read snapshots earlier in recompute.
* Arrays are replaced whole.

---

## Change Model

* Providers may emit change notifications (file watcher, HTTP polling).
* On provider change, Cocoar recomputes all rules and atomically swaps cache.
* If rule factories depend on current config, provider instances/subscriptions are rebuilt.

---

## Required vs Optional

* Required: failures block recompute for that type.
* Optional: failures tolerated and skipped.
