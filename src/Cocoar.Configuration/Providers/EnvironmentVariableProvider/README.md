# EnvironmentVariableProvider

Project configuration from environment variables, optionally filtered by a prefix.

- Options: `EnvironmentVariableProviderOptions(keyPrefix?)`
- Query: `EnvironmentVariableProviderQueryOptions(keyPrefix?, wrapperPath?)`
- Change semantics: currently no default emissions; primarily used for snapshot reads during recompute. Values are strings at source; deserialization can coerce to primitives via the built-in converter.

## When to use

- Container/Kubernetes or PaaS environments where env vars are the canonical override layer.
- Simple toggles and scalar values.

## Example

```csharp
using Cocoar.Configuration.Providers.EnvironmentVariableProvider;

services.AddCocoarConfiguration(
    // Base from file
    FileSourceProvider.CreateRule<MySection>("./appsettings.json", "MySection"),
    // Env overlay, considering variables like MYAPP_*
    EnvironmentVariableProvider.CreateRule<MySection>(memberPath: "MYAPP")
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
