Providers overview

- Abstractions live under Providers/Abstractions and are the only base types used by all providers.
- Implementations live under Providers/<Name>/* and depend only on the abstractions.
- Pooling/reuse: ProviderRegistry shares instances by provider type + instance options CalculateKey().
- Semantics: Changes() must not emit an initial value; initial compute happens via GetValueAsync during ConfigManager.Initialize().
- EnvironmentVariableProvider nesting separators: double underscore "__" and ":" only (single '_' is treated as a literal; a single leading '_' or ':' right after a prefix is trimmed for convenience). No '.' separator.
Note: The Microsoft IConfigurationSource adapter lives in a separate package (Cocoar.Configuration.MicrosoftAdapter). Use the generic `Rules.FromProvider<...>()` with its types or add an extension method in that package.

Notes for project extraction

- You can move Providers/* into a new project that references Cocoar.Configuration.Abstractions.
- Keep CalculateKey() stable and deterministic for pooling.
- Keep wrapper/member behaviors consistent for shaping nested values.
