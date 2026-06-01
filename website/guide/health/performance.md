---
description: Partial re-evaluation, SHA-256 hash-based change detection, zero steady-state cost, one instance per config type, provider sharing by key, reference-equality reactive pipeline, 300ms debounce
---

# Performance Characteristics

This page describes the qualitative performance characteristics of Cocoar.Configuration — what the system does (and avoids doing) at each stage so you can reason about cost in your application.

## Recompute Cost

Recomputes are triggered by provider changes: a file is modified on disk, an HTTP poll returns new data, or an observable emits a new value. Between changes, the system is completely idle.

**Partial re-evaluation.** When a provider signals a change, the recompute starts from the earliest changed rule forward. Rules before that index replay their last contribution from cache — they are not re-fetched or re-deserialized.

**Hash-based change detection.** The `TransformCache` computes a SHA-256 hash of the transformed bytes after Select/Mount processing. If the hash matches the previous value, the provider change is discarded and no recompute is triggered at all. This means file saves that do not change content, or HTTP polls that return the same payload, are free.

**Steady-state cost is zero.** When no provider signals a change, nothing runs — no timers fire, no polling occurs (file providers use OS-level file system notifications), and no background work is performed.

## Memory Footprint

**One live instance per config type.** Each configuration type has exactly one deserialized instance at any time. Instances are immutable and replaced atomically during recompute. The previous instance becomes eligible for garbage collection immediately.

**Thin reactive wrappers.** `IReactiveConfig<T>` is backed by a `BackplaneReactiveConfig<T>` that holds only a reference to the shared `MasterBackplane` and a cached observable projection — one object per type, allocated once.

**Feature flag singletons.** Feature flag and entitlement classes are created once and cached in a `ConcurrentDictionary<Type, object>` for the lifetime of the application. They hold references to `IReactiveConfig<T>` instances internally, which are themselves singletons.

## Scaling Characteristics

**Provider sharing.** Provider instances are shared by key through the `ProviderRegistry`. For file-based configuration, the key is `{Directory}|{PollingInterval}` — so all rules reading from the same directory share a single `FileSourceProvider` and a single file system monitor. Multiple config types in the same directory do not multiply the number of watchers.

**Linear rule cost.** Rules execute sequentially during a recompute. The total cost scales linearly with the number of rules that need re-evaluation (from the earliest changed rule forward). Rules before the change point replay from cache.

**Independent config types.** Each config type is tracked independently in the `ConfigSnapshot`. There is no cross-type overhead — adding a new config type does not affect the cost of existing types.

**Subscriber filtering.** The reactive pipeline uses `DistinctUntilChanged` with reference equality. When a snapshot is published but a particular type's instance has not changed (same reference), subscribers for that type are not notified. This means subscribers only fire on actual changes to their specific type.

## Reactive Pipeline

**Reference-equality change detection.** The `MasterBackplane` projects each type from the snapshot stream using `DistinctUntilChanged` with a `ReferenceEqualityComparer` — a single `ReferenceEquals` call per type per snapshot, which is O(1).

**Tuple change detection.** `ReactiveTupleConfig<T>` checks each element of the tuple independently using `ReferenceEquals`. A tuple emission occurs only when at least one element's reference has changed. This avoids unnecessary downstream processing when a snapshot update does not affect the types in the tuple.

**Source-generated flag descriptors.** Flag and entitlement metadata (names, descriptions, expiry dates) is emitted by a Roslyn source generator at compile time. The generated `CocoarFlagsDescriptors` class is read once at startup during `Register<T>()` — no reflection occurs during flag evaluation at runtime.

## Debouncing

Rapid changes are coalesced by the `RecomputeCoalescer`. The default debounce interval is **300ms** (configurable via `UseDebounce()`). When multiple providers signal changes within the debounce window, only one recompute fires, starting from the earliest changed rule index.

An additional trailing pass (40ms) catches changes that arrive during a running recompute. This prevents missed updates without doubling work.

## What to Watch For

**Slow required providers block the pipeline.** Rules execute sequentially, and the recompute holds a semaphore. A single slow provider (e.g., an HTTP endpoint with high latency) delays the entire recompute. If this is a concern, consider making the rule optional or increasing the timeout on the provider.

**Large JSON with Select.** When using `.Select("path")` on a large JSON document, the full document is still fetched and parsed — the selection happens after parsing. If only a small subsection is needed from a very large file, consider splitting the file.

**Debounce interval too low.** Setting `UseDebounce()` below the default 300ms can cause rapid recomputes under heavy file modification (e.g., during deployment). The default is a good balance for most workloads. If you observe excessive recompute cycles in your metrics (`cocoar.config.recompute.count`), increase the debounce interval.

**Many rules from different directories.** Each unique directory gets its own `FileSourceProvider` and file system monitor. If your rules read from dozens of separate directories, each one adds a watcher. Consolidating config files into fewer directories reduces OS-level resource usage.
