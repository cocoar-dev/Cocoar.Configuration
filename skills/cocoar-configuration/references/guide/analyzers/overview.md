<!-- Generated from website/guide/analyzers/overview.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# Analyzers & Source Generator

Cocoar.Configuration includes Roslyn analyzers and a source generator out of the box — no separate package install needed. They ship as part of the `Cocoar.Configuration` NuGet package.

The **analyzers** catch configuration mistakes at compile time. The **source generator** produces descriptor metadata for feature flags and entitlements — it's required for flags to work (expiry tracking, health reporting, REST endpoint generation all depend on the generated descriptors).

Both run during compilation — in your IDE and in CI. No runtime cost.

## Diagnostics at a Glance

### Configuration (COCFG)

| ID | Severity | What It Catches |
|---|---|---|
| [COCFG001](./configuration.md#cocfg001) | Warning | Secret path conflicts — non-secret property shadows a `Secret<T>` |
| [COCFG002](./configuration.md#cocfg002) | Error | Rule dependency ordering — rule uses config that isn't loaded yet |
| [COCFG003](./configuration.md#cocfg003) | Warning | Required rule references a resource that may not exist |
| [COCFG005](./configuration.md#cocfg005) | Info | Duplicate unconditional rules for the same type |
| [COCFG006](./configuration.md#cocfg006) | Info | Static provider after dynamic providers (ordering suggestion) |

### Feature Flags (COCFLAG)

| ID | Severity | What It Catches |
|---|---|---|
| [COCFLAG001](./flags.md#cocflag001) | Warning | `ExpiresAt` is not a static `DateTimeOffset` literal |
| [COCFLAG002](./flags.md#cocflag002) | Warning | Abstract type passed to `Register<T>()` |
| [COCFLAG003](./flags.md#cocflag003) | Info | Flag or entitlement property missing `<summary>` XML doc |

## Source Generator

The package also includes a source generator that produces descriptor metadata for registered feature flags and entitlements. See [Flags Diagnostics](./flags.md#source-generator) for details.

## Suppressing Diagnostics

Standard C# suppression mechanisms work:

**In code:**
```csharp
#pragma warning disable COCFG005
rules.For<AppSettings>().FromFile("a.json"),
rules.For<AppSettings>().FromFile("b.json")
#pragma warning restore COCFG005
```

**Via attribute:**
```csharp
[SuppressMessage("Cocoar.Configuration", "COCFG005")]
```

**Via .editorconfig:**
```ini
[*.cs]
dotnet_diagnostic.COCFG005.severity = none
```
