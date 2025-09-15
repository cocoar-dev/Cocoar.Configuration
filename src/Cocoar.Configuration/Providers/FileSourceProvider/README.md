# FileSourceProvider

Read JSON from files and watch for changes with debounce.

- Options: `FileSourceProviderOptions(directory, debounceTime)`
- Query: `FileSourceProviderQueryOptions(filename, configurationPath?, debounceTime?)`
- Change semantics: watches the file's directory and emits per-file change signals; debounced to avoid bursts. Transient IO errors in the change stream are swallowed to keep the stream alive. `FetchConfigurationAsync` throws on missing file so required rules can fail appropriately.

## When to use

- Base settings from one or more JSON files.
- Local overrides in dev via additional files layered later in the rule order.

## Examples

```csharp
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;

// 1. Verbose factory form (existing)
services.AddCocoarConfiguration(
    Rules.Using.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", "MySection", debounceTime: TimeSpan.FromMilliseconds(150))).For<MySection>(),
    Rules.Using.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.Local.json", "MySection")).For<MySection>()
);

// 2. New concise overload (uses defaults & optional configurationPath)
services.AddCocoarConfiguration(
    Rule.From.File("./appsettings.json", "MySection").For<MySection>(),
    Rule.From.File("./appsettings.Local.json", "MySection").For<MySection>().Optional()
);

// 3. Without configuration path (root bind) + optional
services.AddCocoarConfiguration(
    Rule.From.File("./myfeature.json").For<MyFeatureSettings>().Optional()
);
```

## Notes

- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- On any emitted change, `ConfigManager` recomputes all rules and atomically swaps the cache.
- See the root `README.md` ("How it works") and `ARCHITECTURE.md` for merge semantics, recompute behavior, and dynamic dependencies.

Known gaps
- Arrays replace prior values; alternate strategies may be added.

## Overload Summary

```csharp
// Factory form (full control; can compute options/query from current in-progress snapshot)
Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(path, configurationPath, debounceTime: ...))

// New concise form (no lambda, quickest path)
Rule.From.File(path, configurationPath?)
```

Prefer the concise form when you just need a simple file + (optional) configuration path. Use the factory when:
- You need a custom debounce time
- You want dynamic path/section selection based on earlier rules
- You need to manipulate provider or query options beyond file + section
