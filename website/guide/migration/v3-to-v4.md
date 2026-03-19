# Migration v3 → v4

v3.x to v4.0 was an incremental release. There are **no breaking changes** to the public API.

## What Changed

v4.0 added new capabilities without modifying existing APIs:

- **Testing Configuration Overrides** — `CocoarTestConfiguration` with `AsyncLocal<T>` isolation
- **Secrets Package** — `Secret<T>` with X.509 hybrid encryption
- **Secrets CLI** — `cocoar-secrets` global tool for encrypting/decrypting
- **Roslyn Analyzers** — COCFG001–006 for compile-time validation

## Internal Breaking Change

The **provider contract** changed from `JsonElement` to `byte[]`:

- `FetchConfigurationAsync` → `FetchConfigurationBytesAsync` (returns `byte[]`)
- `Changes` → `ChangesAsBytes` (emits `byte[]`)

This only affects you if you built a **custom provider** against the v3 contract. Built-in providers were updated automatically.

## Migration

For most applications: update the NuGet package version. No code changes required.

If you have a custom provider, update the two method signatures to use `byte[]` instead of `JsonElement`. See [Building Custom Providers](/guide/providers/custom) for the current contract.
