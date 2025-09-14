# Concepts Deep Dive

* **Rule**: Defines source + optional query + target configuration type.
* **Provider**: Pluggable source (file, environment, HTTP, adapter, static, custom).
* **Merge**: Ordered last-write-wins per flattened key (`Section:Key`) then rebound to your target type.
* **Arrays**: Replaced as whole values (no element-wise merge).
* **Recompute**: Any emitting provider triggers full ordered recompute → atomic snapshot swap.
* **Dynamic dependencies**: Rule factories (options/query) can read in-progress snapshots produced earlier.
* **Required vs Optional**: Required rule failure blocks that config type; optional failure skips the layer.
* **DI Lifetimes & Keys**: Register config types as singleton (default), scoped, transient and/or keyed.

---

## Ordering & Dependencies

* Place dependency-producing rules before dependency-consuming rules.
* Rules may read any type's current snapshot during recompute. Avoid circular dependencies.

**Guidance for recompute-time reads:**

* `GetRequiredConfig<T>()` throws if T does not exist yet; use only if you guarantee T is produced earlier.
* `GetConfig<T>()` returns null if T does not exist.
* Seed dependency types explicitly with static rules to guarantee availability.

---

## Merge Semantics

* Last-write-wins per key using colon flattening.
* Arrays are replace-only.
* Nulls follow default JSON deserialization semantics.
