# Testing Configuration Overrides - Quick Reference

## Overview

`CocoarTestConfiguration` provides zero-ceremony configuration overrides for integration tests using `AsyncLocal<T>` for automatic test isolation.

## Quick Start

```csharp
using Cocoar.Configuration.Testing;

[Fact]
public async Task MyIntegrationTest()
{
    // Set BEFORE creating ConfigManager/WebApplicationFactory
    CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => testDbConfig)
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Original rules are skipped - only test rules execute
}
```

## API

### Replace Mode (Skip Original Rules)
```csharp
CocoarTestConfiguration.ReplaceAllRules(rule => [...]);
```
- Original providers never execute (no I/O, no failures)
- Complete test isolation
- Use when: Providers would fail or you need full control

### Append Mode (Last-Write-Wins)
```csharp
CocoarTestConfiguration.AppendTestRules(rule => [...]);
```
- Original rules run first, test rules override
- Partial overrides only
- Use when: Only specific values need overriding

### Clear Test Configuration
```csharp
CocoarTestConfiguration.Clear();
```
- Always call in test cleanup (`Dispose()`)
- Essential for test isolation

### Check Active Status
```csharp
bool isActive = CocoarTestConfiguration.IsActive;
```

## Works Everywhere

✅ Direct `new ConfigManager(...)`
✅ `services.AddCocoarConfiguration(...)`
✅ `builder.AddCocoarConfiguration(...)`
✅ `new WebApplicationFactory<Program>()`

## Test Base Class Pattern

```csharp
public abstract class IntegrationTestBase : IDisposable
{
    protected void UseTestConfig(Func<RulesBuilder, ConfigRule[]> rules)
        => CocoarTestConfiguration.ReplaceAllRules(rules);

    public virtual void Dispose()
        => CocoarTestConfiguration.Clear();
}
```

## Common Patterns

### Testcontainers
```csharp
[Fact]
public async Task TestWithContainer()
{
    await _postgres.StartAsync();

    CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => new DbConfig
        {
            ConnectionString = _postgres.GetConnectionString()
        })
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Test against testcontainer
}
```

### Feature Flags
```csharp
[Theory]
[InlineData(true)]
[InlineData(false)]
public async Task TestFeatureFlag(bool enabled)
{
    CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<FeatureFlags>().FromStatic(_ => new FeatureFlags
        {
            NewFeature = enabled
        })
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Test with feature on/off
}
```

## Example Project

📁 **[Example Project](../src/Examples/TestingOverridesExample/)**

## Key Benefits

- ✅ Universal (works with any ConfigManager instantiation)
- ✅ Isolated (AsyncLocal per-test context)
- ✅ Zero-ceremony (no test-aware application code)
- ✅ Type-safe (full IntelliSense)
- ✅ Fast (Replace mode skips I/O entirely)
