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
    using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => testDbConfig)
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Original rules are skipped - only test rules execute
    // Automatically cleared when scope is disposed
}
```

## API

### Replace Mode (Skip Original Rules)
```csharp
using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [...]);
```
- Original providers never execute (no I/O, no failures)
- Complete test isolation
- Use when: Providers would fail or you need full control

### Append Mode (Last-Write-Wins)
```csharp
using var _ = CocoarTestConfiguration.AppendTestRules(rule => [...]);
```
- Original rules run first, test rules override
- Partial overrides only
- Use when: Only specific values need overriding

### Apply Existing Context
```csharp
CocoarTestConfiguration.Apply(existingContext);
```
- Sets AsyncLocal from a pre-built `TestConfigurationContext`
- Use in test class constructors for fixture-based patterns

### Clear Test Configuration
```csharp
CocoarTestConfiguration.Clear();
```
- Clears the test configuration
- Not needed if using `using var _` pattern (scope disposes automatically)

### Check Active Status
```csharp
bool isActive = CocoarTestConfiguration.IsActive;
```

---

## Setup Overrides

In addition to overriding rules, you can override **setup** options like `AllowPlaintext()` for secrets. This is useful when your tests need different setup behavior than production.

### Setup-Only Override
```csharp
using var _ = CocoarTestConfiguration.WithSetup(setup => [
    setup.Secrets().AllowPlaintext()
]);
```
- Keeps original rules unchanged
- Only adds/overrides setup options
- Use when: You just need to enable test-specific capabilities

### Rules with Setup Override
```csharp
using var _ = CocoarTestConfiguration.ReplaceAllRules(
    rules => [
        rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test" })
    ],
    setup => [
        setup.Secrets().AllowPlaintext()
    ]);
```
- Replaces rules AND adds setup overrides
- Setup is always merged (not replaced), so configured setup still runs first

### Fixture-Based Pattern with Setup
```csharp
public class IntegrationTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(
            rule => [
                rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" })
            ],
            setup => [
                setup.Secrets().AllowPlaintext()
            ]);
}
```

### How Setup Merging Works

Setup is always **merged** (appended), never replaced:
1. Configured setup runs first
2. Test setup runs after (last-write-wins for capabilities)

This ensures you can override specific setup options without breaking core functionality.

---

## Works Everywhere

- Direct `new ConfigManager(...)`
- `services.AddCocoarConfiguration(...)`
- `builder.AddCocoarConfiguration(...)`
- `new WebApplicationFactory<Program>()`

---

## Understanding AsyncLocal Context

**Important:** AsyncLocal flows automatically through async/await *within the same async context*. However, xUnit creates **separate async contexts** for fixture setup vs test methods.

### The Problem

```csharp
public class MyFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // This runs in async context A
        CocoarTestConfiguration.ReplaceAllRules(rule => [...]);
    }
    // ...
}

public class MyTests : IClassFixture<MyFixture>
{
    [Fact]
    public async Task Test()
    {
        // This runs in async context B - AsyncLocal is NOT visible!
        // ConfigManager won't see test configuration!
    }
}
```

### The Solution: Store and Apply

Store the `TestConfigurationContext` in the fixture, then apply it in the test class constructor (which runs in the test's async context):

```csharp
public class IntegrationTestFixture
{
    // Create context once - can be shared across tests
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" })
        ]);
}

public class MyTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public MyTests(IntegrationTestFixture fixture)
    {
        // Bridge the async context gap - one line!
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task Test()
    {
        // AsyncLocal is now visible in test's context!
        await using var factory = new WebApplicationFactory<Program>();
    }
}
```

---

## Supported Patterns

### Pattern 1: Per-Test Setup (Simple)

Best for simple tests where each test needs different config:

```csharp
public class SimpleTests : IDisposable
{
    [Fact]
    public void Test()
    {
        using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" })
        ]);

        var manager = new ConfigManager(rule => [...]);
        // Uses test rules
    }

    public void Dispose() => CocoarTestConfiguration.Clear();
}
```

### Pattern 2: Fixture-Based (Centralized)

Best for test classes sharing the same config - no base class required:

```csharp
// Fixture holds the shared config context
public class IntegrationTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" }),
            rule.For<ApiSettings>().FromStatic(_ => new ApiSettings { BaseUrl = "https://test.api" })
        ]);
}

// Test class applies it in constructor
public class MyIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public MyIntegrationTests(IntegrationTestFixture fixture)
    {
        // One line to bridge the async context gap!
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task Test1()
    {
        await using var factory = new WebApplicationFactory<Program>();
        // Test config is active
    }

    [Fact]
    public async Task Test2()
    {
        // Same config, no repetition
    }
}
```

### Pattern 3: WebApplicationFactory (ASP.NET Core)

The fixture-based pattern works seamlessly with WebApplicationFactory:

```csharp
public class WebTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=test;Database=TestDb;"
            })
        ]);
}

public class MyWebTests : IClassFixture<WebTestFixture>, IDisposable
{
    public MyWebTests(WebTestFixture fixture)
    {
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task TestEndpoint()
    {
        // AsyncLocal flows through to WebApplicationFactory!
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/data");
        // ConfigManager inside the app sees test config
    }
}
```

### Pattern 4: Using Scope for Automatic Cleanup

The scope pattern provides exception-safe cleanup:

```csharp
[Fact]
public async Task TestWithScope()
{
    using var scope = CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test" })
    ]);

    Assert.True(scope.IsActive);

    await using var factory = new WebApplicationFactory<Program>();
    // ...

    // Automatically cleared when scope is disposed, even on exception
}
```

---

## Common Patterns

### Testcontainers
```csharp
[Fact]
public async Task TestWithContainer()
{
    await _postgres.StartAsync();

    using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
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
    using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<FeatureFlags>().FromStatic(_ => new FeatureFlags
        {
            NewFeature = enabled
        })
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Test with feature on/off
}
```

### Plaintext Secrets in Tests
```csharp
[Fact]
public async Task TestWithPlaintextSecrets()
{
    // Enable plaintext secrets for testing without encryption
    using var _ = CocoarTestConfiguration.ReplaceAllRules(
        rule => [
            rule.For<ApiConfig>().FromStatic(_ => new ApiConfig
            {
                ApiKey = new Secret<string>("test-api-key")
            })
        ],
        setup => [
            setup.Secrets().AllowPlaintext()
        ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Tests can use plaintext secret values
}
```

---

## Factory Methods

Create `TestConfigurationContext` instances for fixture-based patterns:

```csharp
// Replace mode (rules only)
var context = TestConfigurationContext.Replace(rule => [...]);

// Append mode (rules only)
var context = TestConfigurationContext.Append(rule => [...]);

// Replace mode with setup
var context = TestConfigurationContext.Replace(
    rule => [...],
    setup => [setup.Secrets().AllowPlaintext()]);

// Append mode with setup
var context = TestConfigurationContext.Append(
    rule => [...],
    setup => [setup.Secrets().AllowPlaintext()]);
```

---

## Example Project

**[Example Project](../src/Examples/TestingOverridesExample/)**

## Key Benefits

- **Universal** - works with any ConfigManager instantiation
- **Isolated** - AsyncLocal per-test context
- **Zero-ceremony** - no test-aware application code
- **Type-safe** - full IntelliSense
- **Fast** - Replace mode skips I/O entirely
- **Fixture-friendly** - Apply() bridges async context gap
