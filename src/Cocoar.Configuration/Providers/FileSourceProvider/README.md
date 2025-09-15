# FileSourceProvider

Read JSON from files and watch for changes with debounce.

- Options: `FileSourceProviderOptions(directory, debounceTime?)`
- Query: `FileSourceProviderQueryOptions(filename, debounceTime?)`
- Change semantics: watches the file's directory and emits per-file change signals; debounced to avoid bursts. Transient IO errors in the change stream are swallowed to keep the stream alive. `FetchConfigurationAsync` throws on missing file so required rules can fail appropriately.

## When to use

- Base settings from one or more JSON files.
- Local overrides in dev via additional files layered later in the rule order.

## Examples

```csharp
using Cocoar.Configuration.Fluent;

// 1. Factory form + rule-level selection
services.AddCocoarConfiguration(
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", debounceTime: TimeSpan.FromMilliseconds(150)))
        .Select("MySection")
        .For<MySection>()
        .Build(),
    Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("./appsettings.Local.json"))
        .Select("MySection")
        .For<MySection>()
        .Optional()
        .Build()
);

// 2. Concise form + .Select for subsection
services.AddCocoarConfiguration(
    Rule.From.File("./appsettings.json").Select("MySection").For<MySection>().Build(),
    Rule.From.File("./appsettings.Local.json").Select("MySection").For<MySection>().Optional().Build()
);

// 3. Root bind (no selection) + optional
services.AddCocoarConfiguration(
    Rule.From.File("./myfeature.json").For<MyFeatureSettings>().Optional().Build()
);
```

## Notes

- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- On any emitted change, `ConfigManager` recomputes all rules and atomically swaps the cache.
- For subsection binding use `.Select("Section:Sub")`; for relocation use `.MountAt("Root:Sub")`.

## Overload Summary

```csharp
// Factory form (full control)
Rule.From.File(_ => FileSourceRuleOptions.FromFilePath(path, debounceTime: ...)).Select("Section")

// Concise form
Rule.From.File(path).Select("Section") // omit .Select for root binding
```

Prefer the concise form when you need a simple file plus optional subsection selection via `.Select`. Use the factory when:
- You need a custom debounce time.
- You want dynamic filename based on earlier rules.
- You need to compute debounce or other options from current snapshot.
