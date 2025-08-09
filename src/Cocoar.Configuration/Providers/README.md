Providers overview

- Abstractions live under Providers/Abstractions and are the only base types used by all providers.
- Implementations live under Providers/<Name>/* and depend only on the abstractions.
- Pooling/reuse: ProviderRegistry shares instances by provider type + instance options CalculateKey().
- Semantics: Changes() must not emit an initial value; initial compute happens via GetValueAsync during ConfigManager.Initialize().
- EnvironmentVariableProvider supports __, :, and . as separators and uses a constant CalculateKey so only one instance is created.

Notes for project extraction

- You can move Providers/* into a new project that references Cocoar.Configuration.Abstractions.
- Keep CalculateKey() stable and deterministic for pooling.
- Keep MemberPath/MemberWrapper behaviors consistent for wrapping nested values.
