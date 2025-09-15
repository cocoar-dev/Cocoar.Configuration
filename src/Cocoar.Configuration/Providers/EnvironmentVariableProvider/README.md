# EnvironmentVariableProvider

Project configuration from environment variables, optionally filtered by a prefix.

- Options: `EnvironmentVariableProviderOptions(environmentPrefix?)`
- Query: `EnvironmentVariableProviderQueryOptions(environmentPrefix?)`
- Change semantics: currently no default emissions; primarily used for snapshot reads during recompute. Values are strings at source; deserialization can coerce to primitives via the built-in converter.

## When to use

- Container/Kubernetes or PaaS environments where env vars are the canonical override layer.
- Simple toggles and scalar values.

## Examples

```csharp
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Fluent.ProviderOptions;

// 1. Verbose factory form (existing)
services.AddCocoarConfiguration(
    Rules.Using.FromFile(_ => FileSourceRuleOptions.FromFilePath("./appsettings.json", "MySection")).For<MySection>(),
    Rules.Using.FromEnvironment(_ => new EnvironmentVariableRuleOptions(environmentPrefix: "MYAPP")).For<MySection>()
);

// 2. New concise overload (prefix only)
services.AddCocoarConfiguration(
    Rule.From.File("./appsettings.json", "MySection").For<MySection>(),
    Rule.From.Environment("MYAPP").For<MySection>()
);

// 3. No prefix (consume all variables; generally only for controlled test envs)
services.AddCocoarConfiguration(
    Rule.From.Environment().For<RawEnvDump>()
);
```

Environment variable mapping rules (with prefix "MYAPP"):
- Include only variables that start with the prefix: `MYAPP*`.
- Nesting separators: `__` (double underscore) and `:`. A single `_` is literal.
- A single leading separator after the prefix (either `_` or `:`) is trimmed for convenience. For example, `MYAPP__Logging__Level=Debug` and `MYAPP:Logging:Level=Debug` both map to `{ "Logging": { "Level": "Debug" } }`.

## Notes

- The provider exposes strings; `StringToPrimitiveConverter` coerces "true", "42", etc. during deserialization.
- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- See the root `README.md` ("How it works") and `ARCHITECTURE.md` for merge semantics, recompute behavior, and dynamic dependencies.
 - Nested binding via `__` and `:` is supported. This aligns with ASP.NET Core conventions.

Known gaps
- Provider does not emit changes by default; treat as snapshot input. If change-driven recompute is required, combine with other providers.

## Overload Summary

```csharp
// Factory form (dynamic or advanced scenarios)
Rule.From.Environment(_ => new EnvironmentVariableRuleOptions(prefix))

// New concise form
Rule.From.Environment(prefix?)
```

Prefer the concise form when a static (or empty) prefix is sufficient. Use the factory when you need to compute the prefix or adjust query options dynamically using previously built config snapshots.
