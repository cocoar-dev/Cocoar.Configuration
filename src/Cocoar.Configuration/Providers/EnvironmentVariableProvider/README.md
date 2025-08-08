# EnvironmentVariableProvider

Project configuration from environment variables, optionally filtered by a prefix.

- Options: `EnvironmentVariableProviderOptions(prefix?)`
- Query: `EnvironmentVariableProviderQueryOptions(memberPath?, memberWrapper?)`
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

Environment variable mapping rules (with memberPath/prefix "MYAPP"):
- Include only variables that start with `MYAPP_` (single underscore after the prefix).
- The part after the first underscore becomes the property name.
- Example: `MYAPP_Enabled=true` -> `{ "Enabled": true }` (value coerced from string during deserialization)
- Double underscore (`__`) nesting is NOT interpreted; `MYAPP_Nested__Value=42` becomes a flat property named `Nested__Value`.

## Notes

- The provider exposes strings; `StringToPrimitiveConverter` coerces "true", "42", etc. during deserialization.
- Arrays are not merged—only objects. Later rules overwrite earlier keys (last-wins).
- See the root `README.md` and `ARCHITECTURE.md` for merge semantics and the overall change model.
 - Nested binding via `__` (ASP.NET Core style) is currently not supported; use JSON files for nested structures or map flat env vars to flat settings.
