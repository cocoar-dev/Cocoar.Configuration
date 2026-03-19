# Testing Strategy

Test behavior at the right layer, avoid redundancy, and isolate concerns.

## Core Principles

1. **Test YOUR code, not third-party libraries** -- assume external dependencies work
2. **Test at the appropriate layer** -- each test project has a specific responsibility
3. **Avoid redundancy** -- don't re-test the same behavior across multiple layers
4. **Isolate concerns** -- core logic should be testable without I/O dependencies
5. **Add tests when issues are detected** -- not preemptively for every edge case
6. **Deterministic over timing-dependent** -- use active waiting patterns, not fixed delays

## Quick Start

```bash
# Run all tests
dotnet test

# Run only fast unit tests
dotnet test --filter "Type=Unit"

# Run specific provider tests
dotnet test --filter "Provider=ObservableProvider"

# Everything except stress tests
dotnet test --filter "Type!=Stress"
```

## Test Project Structure <Badge type="info" text="ADV" />

### Core.Tests

**Purpose:** Bulletproof the ConfigManager under all conditions -- rule evaluation, merging, reactive behavior, error recovery, and stress/concurrency.

::: warning No I/O in Core.Tests
Use `TestProviders` (in-memory, deterministic) only. Never use `FileSourceProvider`, `HttpProvider`, or other I/O-based providers. This ensures fast execution, deterministic results, and clear failure attribution.
:::

```csharp
[Fact]
public async Task ConfigManager_Handles_Provider_Failure_Gracefully()
{
    var rules = new List<ConfigRule>
    {
        TestRules.Failable<MyConfig>(shouldFail: true),
        TestRules.StaticJson<MyConfig>(fallbackJson)
    };

    var manager = ConfigManager.Create(c => c.UseConfiguration(rules));
    var config = manager.GetConfig<MyConfig>();

    Assert.NotNull(config);
    Assert.Equal(expectedFallbackValue, config.SomeProperty);
}
```

::: tip Why stress-test "simple" providers?
Stress-testing providers validates integration points under ConfigManager's debounce/cancellation logic. When 100+ recompute signals hit simultaneously, it creates race conditions. Proving providers survive any stress means integration test failures must be in higher-level logic.
:::

### Provider.Tests

**Purpose:** Test individual provider implementations -- file watching, HTTP polling, argument parsing, error handling, and query options.

I/O is acceptable and expected here. Providers inherently depend on files, HTTP, and environment variables. Use temporary directories and mock HTTP handlers.

::: warning Don't re-test ConfigManager logic
Test that providers return configuration bytes correctly. Don't test rule merging or orchestration -- that belongs in Core.Tests.
:::

```csharp
[Fact]
public async Task FileProvider_Detects_File_Changes()
{
    using var tempDir = TempDirectoryHelper.Create();
    var configFile = Path.Combine(tempDir.Path, "config.json");
    File.WriteAllText(configFile, """{"value": 1}""");

    var provider = new FileSourceProvider(new(tempDir.Path));
    var config1 = await provider.FetchConfigurationBytesAsync(new("config.json"));

    File.WriteAllText(configFile, """{"value": 2}""");
    await Task.Delay(100);

    var config2 = await provider.FetchConfigurationBytesAsync(new("config.json"));

    Assert.Equal(1, config1.ToJsonElement().GetProperty("value").GetInt32());
    Assert.Equal(2, config2.ToJsonElement().GetProperty("value").GetInt32());
}
```

### DI.Tests

**Purpose:** Test dependency injection integration -- service registration, lifetimes, keyed services, and interface exposure.

```csharp
[Fact]
public void AsSingleton_Creates_Same_Instance()
{
    var services = new ServiceCollection();
    services.AddCocoarConfiguration(c => c.UseConfiguration(rules => [
        rules.For<MyConfig>().FromStaticJson(json).Required()
    ], setup => [
        setup.ConcreteType<MyConfig>().AsSingleton()
    ]));

    var sp = services.BuildServiceProvider();
    var instance1 = sp.GetRequiredService<MyConfig>();
    var instance2 = sp.GetRequiredService<MyConfig>();

    Assert.Same(instance1, instance2);
}
```

### Secrets.Tests

**Purpose:** Test encryption/decryption round-trips, certificate loading and validation, secrets provider behavior, and certificate expiration handling. I/O is acceptable for certificate operations.

### Analyzers.Tests

**Purpose:** Test that Roslyn analyzers detect problematic code patterns and that the source generator produces correct output. Uses the Roslyn testing framework.

## Test Organization (Traits)

All tests should use `[Trait]` attributes for CI filtering:

```csharp
[Fact]
[Trait("Type", "Unit")]
[Trait("Provider", "FileSourceProvider")]
public async Task Test_Scenario_ExpectedResult()
```

| Category | Values |
|----------|--------|
| **Type** | `Unit`, `Performance`, `Concurrency`, `Stress` |
| **Provider** | Specific provider name |
| **Component** | `ConfigManager`, `Secrets`, etc. |

## Active Waiting

```csharp
// BAD: Timing-dependent, flaky
await Task.Delay(1000);
if (someCondition) { ... }

// GOOD: Deterministic, fast when condition met
await ActiveWaitHelpers.WaitUntilAsync(
    () => someCondition,
    timeout: TimeSpan.FromSeconds(5),
    description: "waiting for condition");
```

::: warning Debouncing in tests
ConfigManager debounces by design (default 300ms). Test **final state** correctness, not emission counts. Debouncing coalesces rapid changes, so you'll always get fewer emissions than changes. Final state is always deterministic.
:::

```csharp
// CORRECT: Test final state
behaviorSubject.OnNext("""{"Name": "Updated"}""");
Thread.Sleep(500);
var config = manager.GetConfig<MyConfig>();
Assert.Equal("Updated", config.Name);

// WRONG: Assumes no debouncing
Assert.Equal(expectedEmissions, emissions.Count); // Flaky!
```

## What Makes a Good Test

### Tests Behavior, Not Implementation

```csharp
// BAD - Only tests instantiation
var obj = new MyClass();
Assert.NotNull(obj); // Always passes, no value

// GOOD - Tests actual behavior
var service = new MyService();
var result = service.GetConfig();
Assert.Equal(expectedValue, result.Property);
Assert.True(result.IsValid);
```

### Test Name Matches Test Behavior

```csharp
// BAD - Name claims "scoped" but doesn't verify it
[Fact]
public void Service_Is_Scoped()
{
    var instance = provider.GetService<MyService>();
    Assert.NotNull(instance); // Doesn't verify scoped behavior
}

// GOOD - Actually tests scoped behavior
[Fact]
public void Service_Is_Scoped()
{
    using var scope = provider.CreateScope();
    var instance1 = scope.ServiceProvider.GetService<MyService>();
    var instance2 = scope.ServiceProvider.GetService<MyService>();
    Assert.Same(instance1, instance2);
}
```

### Appropriate Assertions for Test Scope

| Layer | Assertion Style |
|-------|----------------|
| **Provider.Tests** | `Assert.NotNull(config)` is often sufficient -- the provider's job is to return config bytes |
| **Core.Tests** | Assert actual values (`Assert.Equal`) and object identity (`Assert.Same`) |
| **DI.Tests** | Verify lifetime behavior with `Assert.Same` (singleton/scoped) and `Assert.NotSame` (transient) |

### Use TestProviders in Core.Tests

```csharp
// GOOD - Uses TestProvider (no I/O)
var rules = new List<ConfigRule>
{
    TestRules.StaticJson<MyConfig>(json),
    TestRules.Observable<MyConfig>(subject),
    TestRules.Failable<MyConfig>(shouldFail: true)
};

// BAD - Uses real I/O in Core.Tests
var rules = new List<ConfigRule>
{
    rules.For<MyConfig>().FromFile("config.json").Required()
};
```

## Common Anti-Patterns

### Testing External Libraries

```csharp
// BAD - Testing that File.WriteAllText works
File.WriteAllText("test.txt", "content");
var content = File.ReadAllText("test.txt");
Assert.Equal("content", content); // Testing .NET, not your code

// GOOD - Testing YOUR code that uses File I/O
var provider = new FileSourceProvider(options);
var config = await provider.FetchConfigurationBytesAsync(query);
Assert.NotNull(config);
```

### Redundant Testing Across Layers

```csharp
// BAD - Testing ConfigManager merging in Provider.Tests
var provider = new FileSourceProvider(options);
// ... complex merging logic test ...
// This belongs in Core.Tests!

// GOOD - Provider.Tests only test provider behavior
var provider = new FileSourceProvider(options);
var config = await provider.FetchConfigurationBytesAsync(query);
Assert.NotNull(config);
```

### Constructor-Only Tests

```csharp
// BAD - Provides no value
var obj = new MyClass();
Assert.NotNull(obj); // DELETE THIS
```

## When to Add Tests

**Always add tests for:**
- New public API methods
- Bug fixes (regression tests)
- Complex logic or algorithms
- Error handling and edge cases
- Breaking changes (to document migration)

**Consider NOT adding tests for:**
- Simple property getters/setters
- Pass-through methods to external libraries
- Obvious constructor behavior
- Trivial wrappers with no logic

::: tip External library bugs
If you discover a bug in an external library: add a test that reproduces the issue, document it, implement a workaround, and let the test prove the workaround works.
:::

## Helper Utilities <Badge type="info" text="ADV" />

### ActiveWaitHelpers

```csharp
// Wait for condition
await ActiveWaitHelpers.WaitUntilAsync(
    () => condition,
    TimeSpan.FromSeconds(5),
    "description");

// Wait for specific value
var result = await ActiveWaitHelpers.WaitForValueAsync(
    () => getValue(),
    TimeSpan.FromSeconds(5),
    "description");
```

### ObservableTestHelpers

```csharp
await ObservableTestHelpers.WaitForValueAsync(
    observable,
    expectedValue,
    timeout: TimeSpan.FromSeconds(5),
    description: "waiting for expected value");
```

## CI/CD Integration <Badge type="info" text="ADV" />

### Pull Request Checks (Fast)

```bash
# Quick feedback - runs in ~1 second
dotnet test --filter "Type=Unit" --logger:trx
```

### Full Test Suite

```bash
# Complete validation including stress tests - ~5 seconds
dotnet test --logger:trx --collect:"XPlat Code Coverage"
```

### Performance Monitoring

```bash
# Check for regressions
dotnet test --filter "Type=Performance" --logger:trx
```

## Test Quality Checklist

When reviewing tests, ask:

1. Does this test validate **behavior**? (Not just instantiation)
2. Is it testing the **right layer**? (Core vs Provider vs DI concerns)
3. Would this test **fail if there's a bug**? (Or does it always pass?)
4. Is it testing **YOUR code**? (Not third-party libraries)
5. Is the **test name accurate**? (Does it match what's actually tested?)
