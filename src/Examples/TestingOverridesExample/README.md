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
- ✅ Direct `new ConfigManager(...)` instantiation
- ✅ `services.AddCocoarConfiguration(...)` in DI
- ✅ `builder.AddCocoarConfiguration(...)` in ASP.NET Core
- ✅ `WebApplicationFactory<Program>` in integration tests

### Option 1: Replace All Rules (Skip Original Providers)

```csharp
[Fact]
public async Task TestWithReplacedConfig()
{
    // Set test configuration BEFORE creating ConfigManager
    CocoarTestConfiguration.ReplaceAllRules(rule => [
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
    CocoarTestConfiguration.AppendTestRules(rule => [
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

### Option 3: Setup-Only Override

```csharp
[Fact]
public async Task TestWithSetupOverride()
{
    // Override setup options only, keep original rules
    CocoarTestConfiguration.WithSetup(setup => [
        setup.Secrets().AllowPlaintext()
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Original rules execute, but setup includes test overrides
}
```

**Use when:**
- You need to enable test-specific capabilities (e.g., plaintext secrets)
- Original rules should still execute
- No rule changes needed

### Option 4: Rules with Setup Override

```csharp
[Fact]
public async Task TestWithRulesAndSetup()
{
    // Override both rules AND setup
    CocoarTestConfiguration.ReplaceAllRules(
        rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig {
                ConnectionString = testDb
            })
        ],
        setup => [
            setup.Secrets().AllowPlaintext()
        ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Test rules execute with test setup options
}
```

**Use when:**
- You need both rule overrides and setup overrides
- Testing with plaintext secrets in test fixtures

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
5. **Clean Up** - Call `CocoarTestConfiguration.Clear()` or implement `IDisposable`

## Direct ConfigManager Usage

Test overrides also work when creating ConfigManager directly (without DI):

```csharp
[Fact]
public void DirectConfigManagerTest()
{
    CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => testConfig)
    ]);

    // Works with direct instantiation
    var configManager = new ConfigManager(rule => [
        rule.For<DbConfig>().FromFile("config.json") // SKIPPED in test
    ]);
    configManager.Initialize();

    var config = configManager.GetRequiredConfig<DbConfig>();
    // config comes from test rules, not file
}
```

## Using Scope Pattern (Recommended)

The methods now return a `TestConfigurationScope` that clears configuration when disposed:

```csharp
[Fact]
public async Task TestWithScope()
{
    using var _ = CocoarTestConfiguration.ReplaceAllRules(rule => [
        rule.For<DbConfig>().FromStatic(_ => testConfig)
    ]);

    await using var factory = new WebApplicationFactory<Program>();
    // Configuration automatically cleared when scope is disposed
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
            ],
            setup => [
                setup.Secrets().AllowPlaintext()  // Enable plaintext secrets for all tests
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

## Test Base Class Pattern

```csharp
public abstract class IntegrationTestBase : IDisposable
{
    protected void UseTestConfig(Func<RulesBuilder, ConfigRule[]> rules)
    {
        CocoarTestConfiguration.ReplaceAllRules(rules);
    }

    public void Dispose()
    {
        CocoarTestConfiguration.Clear();
    }
}

public class MyTests : IntegrationTestBase
{
    [Fact]
    public async Task MyTest()
    {
        UseTestConfig(rule => [
            rule.For<DbConfig>().FromStatic(_ => testConfig)
        ]);

        await using var factory = new WebApplicationFactory<Program>();
        // Test runs with overridden config
    }
}
```

## Related

- [BasicUsage Example](../BasicUsage) - Simple configuration setup
- [Cocoar.Configuration.Testing](../../Cocoar.Configuration/Testing) - Testing API reference
