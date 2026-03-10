# Testing Overrides Example

This example demonstrates how to override Cocoar configuration in integration tests using `CocoarTestConfiguration`.

## Problem Solved

When writing integration tests with `WebApplicationFactory<T>`, you often need to:
- Replace production configuration with test-specific values
- Skip providers that would fail in test environments (HTTP endpoints, missing files)
- Partially override some configs while keeping others from the original sources

Standard ASP.NET Core allows this:
```csharp
new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder => {
        builder.UseSetting("ConnectionStrings:Postgres", testConnectionString);
    });
```

But with Cocoar.Configuration, you want the same capability using the rule-based API.

## Solution: CocoarTestConfiguration

Set test configuration **before** creating `ConfigManager` (or `WebApplicationFactory`) using `AsyncLocal` context.

**Works universally:**
- ✅ Direct `ConfigManager.Create(...)` instantiation
- ✅ `ConfigManager.CreateAsync(...)` async factory
- ✅ `services.AddCocoarConfiguration(...)` in DI
- ✅ `builder.AddCocoarConfiguration(...)` in ASP.NET Core
- ✅ `WebApplicationFactory<Program>` in integration tests

### Option 1: Replace All Rules (Skip Original Providers)

```csharp
[Fact]
public async Task TestWithReplacedConfig()
{
    // Set test configuration BEFORE creating ConfigManager
    using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
        rule.For<DbConfig>().FromStatic(_ => new DbConfig {
            ConnectionString = testDb
        })
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Original rules (FromFile, FromHttp, etc.) are SKIPPED
    // Only test rules run
}
```

**Use when:**
- Original providers would fail (HTTP endpoint unavailable, files missing)
- You want complete test isolation
- Performance: faster tests (no I/O from original providers)

### Option 2: Append Test Rules (Partial Override)

```csharp
[Fact]
public async Task TestWithPartialOverride()
{
    // Append test rules to end (last-write-wins)
    using var _ = CocoarTestConfiguration.AppendConfiguration(rule => [
        rule.For<DbConfig>().FromStatic(_ => new DbConfig {
            ConnectionString = testDb
        })
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Original rules run first, then test rules override specific values
}
```

**Use when:**
- You only need to override some configuration values
- Other configs can come from original sources (files, environment)
- Partial testing scenarios

### Option 3: Replace Secrets Setup (Independent of Rule Mode)

```csharp
[Fact]
public async Task TestWithPlaintextSecrets()
{
    // Override secrets setup only — original rules still run
    using var _ = CocoarTestConfiguration
        .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());

    await using var factory = new WebApplicationFactory<Program>();
    // Secrets can now be provided as plaintext in test fixtures
}
```

**Use when:**
- You need to enable test-specific secrets behavior (e.g., plaintext)
- Original rules should still execute
- Requires `Cocoar.Configuration.Secrets` package

### Option 4: Mix and Match (Per-Concern Independence)

Each concern is independent — combine freely:

```csharp
[Fact]
public async Task TestWithRulesAndSecrets()
{
    // Replace rules AND override secrets setup independently
    using var _ = CocoarTestConfiguration
        .ReplaceConfiguration(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig {
                ConnectionString = testDb
            })
        ])
        .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());

    await using var factory = new WebApplicationFactory<Program>();
}
```

```csharp
[Fact]
public async Task TestAppendWithSecrets()
{
    // Append rules AND override secrets setup
    using var _ = CocoarTestConfiguration
        .AppendConfiguration(rule => [
            rule.For<FeatureFlags>().FromStatic(_ => new FeatureFlags { NewFeature = true })
        ])
        .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());

    await using var factory = new WebApplicationFactory<Program>();
}
```

## Running the Example

```bash
cd src/Examples/TestingOverridesExample
dotnet test
```

## Key Points

1. **Universal Application** - Works with any ConfigManager instantiation method (direct, DI, AspNetCore)
2. **AsyncLocal Context** - Test configuration flows through async/await automatically
3. **No Application Changes** - `Program.cs` doesn't need test-aware code
4. **Per-Test Isolation** - Each test can set different configuration
5. **Per-Concern Independence** - Rules mode and secrets setup override independently
6. **Clean Up** - `using var _` disposes the scope; or call `CocoarTestConfiguration.Clear()`

## Direct ConfigManager Usage

Test overrides also work when creating ConfigManager directly (without DI):

```csharp
[Fact]
public void DirectConfigManagerTest()
{
    using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
        rule.For<DbConfig>().FromStatic(_ => testConfig)
    ]);

    // Works with direct instantiation
    var configManager = ConfigManager.Create(c => c.UseConfiguration(rule => [
        rule.For<DbConfig>().FromFile("config.json") // SKIPPED in test
    ]));

    var config = configManager.GetConfig<DbConfig>()!;
    // config comes from test rules, not file
}
```

Also works with `ConfigManager.CreateAsync()`:

```csharp
[Fact]
public async Task DirectConfigManagerAsyncTest()
{
    using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
        rule.For<DbConfig>().FromStatic(_ => testConfig)
    ]);

    var configManager = await ConfigManager.CreateAsync(c => c.UseConfiguration(rule => [
        rule.For<DbConfig>().FromFile("config.json") // SKIPPED in test
    ]));

    var config = configManager.GetConfig<DbConfig>()!;
}
```

## Fixture-Based Pattern (Centralized Config)

For test classes sharing the same configuration, use fixtures with `Apply()`:

```csharp
// Fixture holds the shared config context
public class IntegrationTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(
            rule => [
                rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" }),
                rule.For<ApiSettings>().FromStatic(_ => new ApiSettings { BaseUrl = "https://test.api" })
            ]);
}

// Test class applies it in constructor
public class MyTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public MyTests(IntegrationTestFixture fixture)
    {
        // Bridge the async context gap
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task Test1()
    {
        await using var factory = new WebApplicationFactory<Program>();
        // Uses fixture's config
    }

    [Fact]
    public async Task Test2()
    {
        // Same config, no repetition
    }
}
```

**Why Apply()?** AsyncLocal flows within the same async context, but xUnit runs fixture setup and test methods in separate contexts. `Apply()` bridges this gap.

## Fixture-Based Pattern with Secrets

Use `TestOverrideBuilder` directly for fixture-based patterns that need secrets:

```csharp
public class IntegrationTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        new TestOverrideBuilder()
            .ReplaceConfiguration(rule => [
                rule.For<DbConfig>().FromStatic(_ => new DbConfig { Connection = "test-db" })
            ])
            .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext())
            .Build();
}

public class MyTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public MyTests(IntegrationTestFixture fixture)
    {
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();
}
```

## Related

- [BasicUsage Example](../BasicUsage) - Simple configuration setup
- [Cocoar.Configuration.Testing](../../Cocoar.Configuration/Testing) - Testing API reference
- [Testing Overrides Quick Reference](../../../docs/testing-overrides-quickref.md) - Full patterns
