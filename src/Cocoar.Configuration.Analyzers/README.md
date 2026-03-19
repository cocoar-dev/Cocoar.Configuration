# Cocoar.Configuration.Analyzers

Roslyn analyzers for Cocoar.Configuration that provide **compile-time validation** of configuration rules.

## Features

- **COCFG001**: Detects secret property path conflicts
- **COCFG002**: Validates rule dependency ordering
- **COCFG003**: Checks required rule configuration
- **COCFG005**: Detects duplicate unconditional rules (last-write-wins warning)
- **COCFG006**: Static provider ordering suggestions
- **Compile-time diagnostics**: Catches configuration mistakes before runtime

## Installation

```xml
<PackageReference Include="Cocoar.Configuration.Analyzers" Version="3.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Benefits

✅ **Compile-time validation** - Catch configuration errors while coding  
✅ **IDE integration** - Red squiggles in Visual Studio, Rider, VS Code  
✅ **Zero runtime cost** - No reflection or analysis at startup  
✅ **Actionable messages** - Clear guidance on how to fix each issue
✅ **CI/CD integration** - Build fails on misconfiguration  

## Diagnostics

### COCFG001: Secret Path Conflict

**Severity:** Warning

Detects when a non-secret property has the same path as a secret property.

```csharp
// ❌ Warning:
rule.For<AppSettings>().FromFile("app.json").Select("Database.Password"),
rule.For<Secrets>().FromFile("secrets.json")
// COCFG001: Property 'Password' conflicts with secret 'Secrets.ConnectionString'
```

### COCFG002: Rule Dependency Ordering

**Severity:** Error

Validates that rules appear after their dependencies.

```csharp
// ❌ Error:
rule.For<DerivedConfig>()
    .When(accessor => accessor.GetRequiredConfig<ApiSettings>().IsEnabled),
rule.For<ApiSettings>().FromFile("api.json"),
// COCFG002: ApiSettings not available - move this rule after ApiSettings rule
```

### COCFG003: Required Rule Validation

**Severity:** Warning

Checks required rules have valid configuration.

```csharp
// ❌ Warning:
rule.For<AppSettings>()
    .FromFile("missing.json")
    .Required()
// COCFG003: File 'missing.json' not found - app will fail to start
```

### COCFG005: Duplicate Unconditional Rules

**Severity:** Info

Warns when multiple rules configure the same type without conditions (last-write-wins).

```csharp
// ℹ️ Info:
rule.For<AppSettings>().FromFile("appsettings.json"),
rule.For<AppSettings>().FromFile("appsettings.override.json"),
// COCFG005: Multiple unconditional rules for type 'AppSettings'. 
// Last rule will override earlier rules.
```

### COCFG006: Static Provider Ordering

**Severity:** Info

Suggests moving static/seed rules before dynamic rules that may depend on them.

```csharp
// ℹ️ Info:
rule.For<ApiSettings>().FromFile("api.json"),
rule.For<FeatureFlags>().FromStatic("""{"Feature1": true}"""),
// COCFG006: Static rule found after dynamic rules. 
// Consider moving static rules first.
```

## Migration from Runtime Analysis

The analyzer replaces runtime `ConfigurationAnalyzer`:

**Before (Runtime):**
```csharp
ConfigurationAnalyzer.AnalyzeDependencies(rules, logger, accessor); // At startup
```

**After (Compile-time):**
```xml
<!-- Just install analyzer package - errors show in IDE -->
<PackageReference Include="Cocoar.Configuration.Analyzers" Version="3.0.0" />
```

## License

Apache 2.0 - See [LICENSE](https://github.com/cocoar-dev/cocoar.configuration/blob/develop/LICENSE)
