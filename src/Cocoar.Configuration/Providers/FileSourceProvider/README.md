# FileSourceProvider

Read JSON from files and watch for changes with debounce.

- Options: `FileSourceProviderOptions(directory, debounceTime)`
- Query: `FileSourceProviderQueryOptions(filename, memberPath?, memberWrapper?)`
- Change semantics: watches the file's directory and emits per-file change signals; debounced to avoid bursts. Transient IO errors in the change stream are swallowed to keep the stream alive. `GetValueAsync` throws on missing file so required rules can fail appropriately.

## When to use

- Base settings from one or more JSON files.
- Local overrides in dev via additional files layered later in the rule order.

## Example

```csharp
using Cocoar.Configuration.Providers.FileSourceProvider;

services.AddCocoarConfiguration(
    FileSourceProvider.CreateRule<MySection>(
        filepath: "./appsettings.json",
        memberPath: "MySection",
        debounceTime: TimeSpan.FromMilliseconds(150)
    ),
    FileSourceProvider.CreateRule<MySection>(
        filepath: "./appsettings.Local.json",
        memberPath: "MySection"
    )
);
```

## Notes

- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- On any emitted change, `ConfigManager` recomputes all rules and atomically swaps the cache.
- See the root `README.md` ("How it works") and `ARCHITECTURE.md` for merge semantics, recompute behavior, and dynamic dependencies.

Known gaps
- Arrays replace prior values; alternate strategies may be added.
