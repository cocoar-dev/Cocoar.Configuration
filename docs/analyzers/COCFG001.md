# COCFG001 — Secret Path Conflict Detected

## What it detects

Two or more configuration rules target the same type and include overlapping secret paths,
which can cause one rule's secrets to silently overwrite another's after decryption.

## Why it matters

Secret values are decrypted and stored in memory. If two rules contribute secrets to the
same configuration type with conflicting paths, the last rule wins silently, potentially
discarding secrets that were meant to be additive.

## Example

### Non-compliant
```csharp
rules.For<DbConfig>().FromFile("base.json"),   // contains ConnectionString secret
rules.For<DbConfig>().FromFile("local.json")   // also contains ConnectionString secret
```

### Compliant
```csharp
// Use MountAt to give each rule its own namespace, or ensure only one rule owns the secret path
rules.For<DbConfig>().FromFile("base.json").MountAt("Base"),
rules.For<DbConfig>().FromFile("local.json").MountAt("Override")
```

## How to fix

Ensure that each rule contributing secrets to a shared type mounts its values under a unique path,
or consolidate secrets into a single authoritative rule.
