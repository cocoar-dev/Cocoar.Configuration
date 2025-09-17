# Provider Development Guide

Guidance for building third‑party providers:

* Implement generic provider base.
* Add fluent API entry points (e.g. `Rule.From.MyProvider()`).
* Manage provider lifecycle & change emission.
* Provide tests for deterministic merge semantics.
* Prefer emitting only when the effective selected data changes if inexpensive to detect (engine still performs selection-hash gating to suppress no-ops).

---

## Fluent API Extensions

Keep API surface consistent with existing providers.

---

## Instance Lifecycle

Providers may be pooled across recomputes. Use `IDisposable` for cleanup.

---

## Testing Strategies

* Unit test provider emission
* Integration test recompute behavior
* Ensure deterministic last-write-wins semantics
