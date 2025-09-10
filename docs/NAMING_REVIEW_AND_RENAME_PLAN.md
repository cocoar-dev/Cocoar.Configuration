# Cocoar.Configuration – Naming audit and rename plan (2025-09-10)

This document captures naming inconsistencies and concrete rename suggestions across code and docs. It’s designed to be a source of truth for a systematic refactor (types, members, namespaces, files, and docs).

Scope reviewed:
- Core: `src/Cocoar.Configuration/**`
- Providers: FileSourceProvider, EnvironmentVariableProvider, StaticJsonProvider
- Packages: HttpPolling, MicrosoftAdapter, AspNetCore
- Docs: root `README.md`, core `README.md`, `ARCHITECTURE.md`, providers’ READMEs, `docs/AGREED_BEHAVIOR_AND_FOLLOWUPS.md`

## Principles
- Be explicit and consistent: pick one term per concept and use it everywhere.
- Reserve “Prefix” for flat key prefixes (Microsoft IConfiguration, environment vars).
- Reserve “Section”/“Property” for selecting a sub-object within JSON payloads.
- Reserve “Wrapper” for shaping the result under a new property in the merged output.
- File/class/namespace names should align 1:1.

## Global terminology standard
- Payload sub-selection within an object JSON: SectionPropertyName (single top-level property name). If/when path semantics are implemented, use SectionPath (colon `:`-separated) consistently.
- Wrapper key for nesting the result: WrapperKey (a single property name). If path semantics are implemented, use WrapperPath consistently.
- Flat key filtering/selection in “flattened” sources (Microsoft IConfiguration, env vars): KeyPrefix.

## High-priority issues and proposals

1) Config contract vs registration naming
- Problem: `ConfigTypeDefinition(ConfigType, ImplementationType?)` uses misleading names. In practice, `ConfigType` holds the concrete type to deserialize to; `ImplementationType` is used as the interface/contract alias for DI registration. This reads backwards.
- Proposal: Rename type and members.
  - Type: `ConfigTypeDefinition` → `ConfigRegistration`
  - Members: `ConfigType` → `ConcreteType`; `ImplementationType` → `ContractType`
  - Equality/HashCode: key on `ConcreteType`.
  - Update all usages: `ConfigRule.ConfigContract` stays but consider renaming to `Registration` for clarity.

2) Query option names: “SectionPath/KeyPrefix/WrapperPath” inconsistencies
- Problem: Mixed terms across providers and docs (`SectionPath`, `KeyPrefix`, `memberPath`, `memberWrapper`, `WrapperPath`). Some members imply path semantics, but implementations often only support a single top-level property (TryGetProperty).
- Proposal: Normalize now to “single property” semantics, with future-proof notes.
  - ISourceProviderQueryOptions.WrapperPath → WrapperKey (single property name). If path support is later added, reintroduce WrapperPath and keep WrapperKey as alias.
  - FileSourceProviderQueryOptions.SectionPath → SectionPropertyName
  - FileSourceProviderQueryOptions.WrapperPath → WrapperKey
  - FileSourceProviderQueryOptions.Debounce → DebounceTime (align with options)
  - HttpPollingProviderQueryOptions.KeyPrefix → SectionPropertyName (this API selects a property of the fetched JSON)
  - HttpPollingProviderQueryOptions.UrlPathOrAbsolute → RelativeOrAbsoluteUrl
  - MicrosoftConfigurationSourceProviderQueryOptions.KeyPrefix: keep as-is (it’s a flat key prefix in IConfiguration, not a JSON object section). Keep WrapperPath or switch to WrapperKey per above.
  - EnvironmentVariableProviderQueryOptions.KeyPrefix: keep (env var prefix filter), rename its WrapperPath → WrapperKey.
  - ConfigSourceProvider.WrapIfNeeded parameter names and XML docs: rename from “memberWrapper” to “wrapperKey/WrapperKey”.

3) HttpPollingProvider selection bug (semantics vs naming)
- Problem: `HttpPollingProvider.GetValueAsync` uses `WrapperPath` to select a sub-property, then also wraps with `WrapperPath`. `Changes()` uses `KeyPrefix` (intended). This is both a bug and a naming confusion.
- Proposal: After applying proposal (2), fix semantics:
  - Select with `SectionPropertyName` (if provided): pick the single top-level property before wrapping.
  - Wrap with `WrapperKey` (if provided).

4) Namespace consistency for fluent builders and options
- Problem: Builders/options live in mixed namespaces:
  - File: `Cocoar.Configuration.Providers.FileSourceProvider.Fluent` (builder+options)
  - Environment builder: `Cocoar.Configuration.Fluent.Providers`, options: `Cocoar.Configuration.Fluent.ProviderOptions`
  - HTTP: `Cocoar.Configuration.HttpPolling` (builder), extension in same package
  - Microsoft: `Cocoar.Configuration.MicrosoftAdapter.Fluent`
- Proposal: Use provider-centric namespaces consistently: `Cocoar.Configuration.[Provider].Fluent` for provider-owned DSL types.
  - Move `EnvironmentRuleBuilder` → `Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent`
  - Move `EnvironmentVariableRuleOptions` → `Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent`
  - Move `HttpRuleBuilder` → `Cocoar.Configuration.HttpPolling.Fluent`
  - Keep `Rules` host in `Cocoar.Configuration.Fluent` as-is.

  6) Duplicate DI extension classes and namespaces
  - Problem: Two public `CocoarConfigurationExtensions` types exist with identical methods but different namespaces:
    - `namespace Cocoar.Configuration` in `src/Cocoar.Configuration/CocoarConfigurationExtensions.cs`
    - `namespace Cocoar.Configuration.Extensions` in `src/Cocoar.Configuration/Extensions/CocoarConfigurationExtensions.cs`
  - Both also define `ThrowIfAlreadyRegistered(...)`. This can cause ambiguous extension resolution for consumers who import both namespaces.
  - Proposal: Keep one authoritative class (prefer `Cocoar.Configuration.CocoarConfigurationExtensions`) and remove the duplicate. If you want a dedicated `.Extensions` namespace, keep only that one and delete/move the other. Update docs accordingly.

5) File/class name mismatches
- Problem: Some files don’t match contained type names.
  - `Providers/FileSourceProvider/Observable/FileWatcherObservable.cs` contains `FileSystemObservable`.
  - `Cocoar.Configuration.AspNetCore/CocoarConfigurationExtensions.cs` contains `CocoarConfigurationAspNetCoreExtensions`.
- Proposal: Rename files to match types: `FileSystemObservable.cs`, `CocoarConfigurationAspNetCoreExtensions.cs`.

6) DI extension namespace vs documentation
- Problem: Core DI extension lives in `namespace Cocoar.Configuration` but docs mention `Cocoar.Configuration.Extensions`.
- Options:
  - A) Move the type into `Cocoar.Configuration.Extensions` namespace (breaking) and keep a type-forwarder/partial wrapper; or
  - B) Update docs to reflect the current `Cocoar.Configuration` namespace.
- Proposal: Choose B (doc update) unless there’s a strong desire for a dedicated `.Extensions` namespace.

7) AspNetCore extension parameter names
- Problem: In `Cocoar.Configuration.AspNetCore.CocoarConfigurationAspNetCoreExtensions`, two overloads name the `WebApplicationBuilder` parameter `services` instead of `builder`.
- Proposal: Rename the parameter to `builder` for clarity and consistency across overloads.

8) Environment variable separator docs mismatch
- Problem: Providers README says “no '.' separator”, but implementation treats `.` as a separator (and trims a single leading `_`/`:` after prefix). Root docs suggest `__` and `:` only.
- Proposal: Update provider docs to match code: separators are `__` and `:`, and `.` is also treated as a separator by implementation. If `.` should not be a separator, adjust code accordingly.

9) “UseWhen” fluent naming
- Optional: `RuleBuilderBase.When(...)` vs `ConfigRuleOptions.UseWhen`. Consider `When(...)` → `UseWhen(...)` for symmetry. Low priority; fluent naming is acceptable as-is.

10) ThrowIfAlreadyRegistered naming
- Optional: `ThrowIfAlreadyRegistered` is generic for an IServiceCollection extension. Consider `ThrowIfCocoarAlreadyRegistered` to avoid ambiguity in intellisense. Low priority.

11) Env provider instance options contain unused KeyPrefix
- Problem: `EnvironmentVariableProviderOptions` has `KeyPrefix` but provider logic ignores it (filtering is handled by query options), and `CalculateKey()` returns a constant, making the property misleading.
- Proposal: Remove `KeyPrefix` from instance options (or repurpose it into the identity if you want separate instances per prefix). Keep prefix strictly in query options.
- Optional: `ThrowIfAlreadyRegistered` is generic for an IServiceCollection extension. Consider `ThrowIfCocoarAlreadyRegistered` to avoid ambiguity in intellisense. Low priority.

## Concrete rename map (source of truth)

Legend: Type -> New Type, Member -> New Member, Namespace -> New Namespace, File -> New File. Paths are relative to `src/` unless noted.

### A. Types and members

- Type: `Cocoar.Configuration.ConfigTypeDefinition` → `Cocoar.Configuration.ConfigRegistration`
  - Member: `ConfigType` → `ConcreteType`
  - Member: `ImplementationType` → `ContractType`
  - All equality/hash logic should key on `ConcreteType`.

- Interface: `Cocoar.Configuration.Providers.Abstractions.ISourceProviderQueryOptions`
  - Member: `WrapperPath` → `WrapperKey`

- Class: `Cocoar.Configuration.Providers.Abstractions.ConfigSourceProvider`
  - Method param/name/docs: `WrapIfNeeded(..., string? memberWrapper)` → `WrapIfNeeded(..., string? wrapperKey)`

- Record: `Cocoar.Configuration.Providers.FileSourceProvider.FileSourceProviderQueryOptions`
  - Member: `SectionPath` → `SectionPropertyName`
  - Member: `WrapperPath` → `WrapperKey`
  - Member: `Debounce` → `DebounceTime`

- Class: `Cocoar.Configuration.Providers.FileSourceProvider.FileSourceProviderOptions`
  - Member: `Directory` → `RootDirectory` (and adjust all references)

- Class: `Cocoar.Configuration.HttpPolling.HttpPollingProviderQueryOptions`
  - Member: `UrlPathOrAbsolute` → `RelativeOrAbsoluteUrl`
  - Member: `KeyPrefix` → `SectionPropertyName`
  - Member: `WrapperPath` → `WrapperKey`

- Class: `Cocoar.Configuration.HttpPolling.HttpPollingRuleOptions`
  - Member: `UrlPathOrAbsolute` → `RelativeOrAbsoluteUrl`
  - Member: `SectionPath` → `SectionPropertyName`
  - Member: `WrapperPath` → `WrapperKey`

- Class: `Cocoar.Configuration.MicrosoftAdapter.MicrosoftConfigurationSourceProviderQueryOptions`
  - Keep: `KeyPrefix` (flat key prefix). Optionally align `WrapperPath` → `WrapperKey` for consistency.

- Class: `Cocoar.Configuration.Providers.EnvironmentVariableProvider.EnvironmentVariableProviderQueryOptions`
  - Keep: `KeyPrefix` (env var prefix filter); rename `WrapperPath` → `WrapperKey`.

- Class: `Cocoar.Configuration.ConfigRule` (properties)
  - Member: `ConfigContract` → `Registration` (if `ConfigTypeDefinition` → `ConfigRegistration`).

### B. Namespaces (fluent providers)

- Environment fluent
  - Type: `Cocoar.Configuration.Fluent.Providers.EnvironmentRuleBuilder` → `Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent.EnvironmentRuleBuilder`
  - Type: `Cocoar.Configuration.Fluent.ProviderOptions.EnvironmentVariableRuleOptions` → `Cocoar.Configuration.Providers.EnvironmentVariableProvider.Fluent.EnvironmentVariableRuleOptions`

- HTTP fluent
  - Type: `Cocoar.Configuration.HttpPolling.HttpRuleBuilder` → `Cocoar.Configuration.HttpPolling.Fluent.HttpRuleBuilder`

- File fluent (already consistent)
  - Keep: `Cocoar.Configuration.Providers.FileSourceProvider.Fluent.*`

### C. Files

- File: `src/Cocoar.Configuration/Providers/FileSourceProvider/Observable/FileWatcherObservable.cs` → `FileSystemObservable.cs`
- File: `src/Cocoar.Configuration.AspNetCore/CocoarConfigurationExtensions.cs` → `CocoarConfigurationAspNetCoreExtensions.cs`

### D. Documentation corrections (non-code)

- Replace “memberPath/memberWrapper” with “SectionPropertyName/WrapperKey” throughout README and provider docs.
- Clarify that current implementations select a single top-level property; nested “path” selection is not yet implemented. If you want true path support, plan that as a separate change.
- Environment variable separators: document `__`, `:`, and `.` as separators (single `_` is literal; a single leading `_`/`:` after prefix is trimmed). Align all docs accordingly.
- DI extension namespace: update docs to show `using Cocoar.Configuration;` for `AddCocoarConfiguration(...)` unless you adopt the `.Extensions` namespace move.

## Dependency and impact notes

- The `ConfigTypeDefinition` → `ConfigRegistration` change is cross-cutting (ConfigManager, DI extensions, tests). Plan this first.
- Query option renames touch all providers and the fluent layer. Consider introducing aliases/obsolete members to ease migration.
- Namespace moves for fluent builders will require project reference updates in any downstream consumers. The extension methods (`Rules.Using.*`) will continue to work once namespaces are imported.
- File renames are straightforward; adjust `.csproj` includes if any were explicit (most are implicit SDK-style).

## Suggested migration order
1) Introduce new names alongside old ones with [Obsolete] attributes (no build breaks), update internal usages, then remove old names in a major bump.
2) Fix `HttpPollingProvider.GetValueAsync` to use the selection member (proposed `SectionPropertyName`) instead of `WrapperPath`.
3) Align all docs to the normalized terminology.
4) Namespace/file renames for fluent builders and observables.
5) Optional: rename `ThrowIfAlreadyRegistered` and `When(...)` if desired.

## Example diffs (high-level)

- Config registration
  - Before: `new ConfigTypeDefinition(typeof(MyConcrete), typeof(IMyContract))`
  - After:  `new ConfigRegistration(typeof(MyConcrete), typeof(IMyContract))`

- File provider query options
  - Before: `new FileSourceProviderQueryOptions("appsettings.json", SectionPath: "My", WrapperPath: null, Debounce: TimeSpan.FromMilliseconds(50))`
  - After:  `new FileSourceProviderQueryOptions("appsettings.json", SectionPropertyName: "My", WrapperKey: null, DebounceTime: TimeSpan.FromMilliseconds(50))`

- HTTP provider query options
  - Before: `new HttpPollingProviderQueryOptions("/v1/settings", keyPrefix: "Remote", wrapperPath: "My", headers: ...)`
  - After:  `new HttpPollingProviderQueryOptions("/v1/settings", sectionPropertyName: "Remote", wrapperKey: "My", headers: ...)`

## Open questions for maintainers
- Do you want true path semantics (colon-separated) for selection/wrapping? If yes, implement in providers and keep the names `SectionPath`/`WrapperPath` instead of `SectionPropertyName`/`WrapperKey`.
- Do you prefer moving DI extensions into a `.Extensions` namespace to match docs, or adjust docs to current reality?
- Should `HttpPollingProviderOptions.BaseAddress` switch to `Uri` for stronger typing?

---

Last updated: 2025-09-10
Author: automated naming audit