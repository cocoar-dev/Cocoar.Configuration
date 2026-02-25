# Testing Strategy

> **Philosophy:** Test behavior at the right layer, avoid redundancy, and isolate concerns.

---

## 🎯 Core Principles

1. **Test YOUR code, not third-party libraries** - Assume external dependencies work
2. **Test at the appropriate layer** - Each test project has a specific responsibility
3. **Avoid redundancy** - Don't re-test the same behavior across multiple layers
4. **Isolate concerns** - Core logic should be testable without I/O dependencies
5. **Add tests when issues are detected** - Not preemptively for every edge case
6. **Deterministic over timing-dependent** - Use active waiting patterns, not fixed delays

---

## 🚀 Quick Start

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

---

## 📁 Test Project Structure

### **Cocoar.Configuration.Core.Tests**

**Purpose:** Bulletproof the ConfigManager under all conditions

**Responsibilities:**
- Test ConfigManager orchestration logic
- Test rule evaluation and merging
- Test reactive configuration behavior
- Test error handling and recovery
- Test under stress, high concurrency, and edge cases

**Key Constraint:** ⚠️ **NO I/O DEPENDENCIES**
- ✅ Use `TestProviders` (in-memory, deterministic)
- ✅ Use `StaticJsonProvider`, `ObservableProvider` with controlled data
- ❌ Never use `FileSourceProvider`, `HttpProvider`, or other I/O-based providers
- ❌ Never depend on file system, network, or external resources

**Why:** Isolating ConfigManager from I/O ensures:
- Fast test execution (no I/O overhead)
- Deterministic results (no flaky I/O failures)
- Clear failure attribution (ConfigManager logic, not provider issues)

**Strategic Philosophy:**
Stress-testing "simple" providers validates integration points under ConfigManager's complex debounce/cancellation logic. When 100+ recompute signals hit ConfigManager, it creates race conditions. Proving providers can handle ANY stress means integration test failures must be in higher-level logic.

**Example Test Pattern:**
```csharp
[Fact]
public async Task ConfigManager_Handles_Provider_Failure_Gracefully()
{
    var rules = new List<ConfigRule>
    {
        TestRules.Failable<MyConfig>(shouldFail: true), // TestProvider, not real I/O
        TestRules.StaticJson<MyConfig>(fallbackJson)
    };
    
    var manager = ConfigManager.Create(c => c.WithConfiguration(rules));
    var config = manager.GetConfig<MyConfig>(); // Should use fallback
    
    Assert.NotNull(config);
    Assert.Equal(expectedFallbackValue, config.SomeProperty);
}
```

---

### **Cocoar.Configuration.Providers.Tests**

**Purpose:** Test individual provider implementations

**Responsibilities:**
- Test provider-specific behavior (file watching, HTTP polling, argument parsing, etc.)
- Test provider instantiation under various configurations
- Test provider error handling
- Test provider query options and parameters

**Key Constraint:** ⚠️ **DO NOT re-test ConfigManager logic**
- ✅ Test that provider returns configuration bytes correctly
- ✅ Test provider-specific features (polling intervals, file watching, etc.)
- ❌ Don't test rule merging (ConfigManager's responsibility)
- ❌ Don't test orchestration logic (ConfigManager's responsibility)

**I/O Usage:** ✅ I/O is acceptable and expected here
- Providers inherently depend on I/O (files, HTTP, environment variables)
- Use temporary directories, mock HTTP handlers, etc.
- Indirectly tests external libraries (e.g., Cocoar.FileSystem) - this is fine

**Example Test Pattern:**
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
    await Task.Delay(100); // Wait for file watcher
    
    var config2 = await provider.FetchConfigurationBytesAsync(new("config.json"));
    
    Assert.Equal(1, config1.ToJsonElement().GetProperty("value").GetInt32());
    Assert.Equal(2, config2.ToJsonElement().GetProperty("value").GetInt32());
}
```

---

### **Cocoar.Configuration.DI.Tests**

**Purpose:** Test dependency injection integration

**Responsibilities:**
- Test service registration (singleton, scoped, transient)
- Test keyed service registration
- Test interface exposure (`ExposeAs<T>`)
- Test reactive configuration registration
- Test automatic registration behavior

**Key Constraint:** ⚠️ **Test DI behavior, not ConfigManager logic**
- ✅ Verify service lifetimes with `Assert.Same()` / `Assert.NotSame()`
- ✅ Test that services resolve correctly
- ❌ Don't re-test configuration merging
- ❌ Don't re-test rule evaluation

**Example Test Pattern:**
```csharp
[Fact]
public void AsSingleton_Creates_Same_Instance()
{
    var services = new ServiceCollection();
    services.AddCocoarConfiguration(c => c.WithConfiguration(rules => [
        rules.For<MyConfig>().FromStaticJson(json).Required()
    ], setup => [
        setup.ConcreteType<MyConfig>().AsSingleton()
    ]));
    
    var sp = services.BuildServiceProvider();
    var instance1 = sp.GetRequiredService<MyConfig>();
    var instance2 = sp.GetRequiredService<MyConfig>();
    
    Assert.Same(instance1, instance2); // ← Verifies singleton behavior
}
```

---

### **Cocoar.Configuration.Secrets.Tests**

**Purpose:** Test encryption/decryption and secrets management

**Responsibilities:**
- Test certificate loading and validation
- Test encryption/decryption round-trips
- Test secrets provider behavior
- Test certificate expiration handling

**Similar constraints to Provider.Tests** - I/O is acceptable for certificate operations.

---

### **Cocoar.Configuration.Analyzers.Tests**

**Purpose:** Test Roslyn analyzers and code fixes

**Responsibilities:**
- Test that analyzers detect problematic code patterns
- Test that code fixes produce correct results
- Use Roslyn testing framework

---

## 🧪 Writing Tests

### Test Organization (Traits)
All tests should use `[Trait]` attributes for CI filtering:

```csharp
[Fact]
[Trait("Type", "Unit")]        // Unit, Performance, Concurrency, Stress
[Trait("Provider", "FileSourceProvider")]
public async Task Test_Scenario_ExpectedResult()
```

**Trait Categories:**
- **Type**: `Unit`, `Performance`, `Concurrency`, `Stress`
- **Provider**: Specific provider name
- **Component**: `ConfigManager`, `Secrets`, etc.

### Active Waiting (Not Fixed Delays)

```csharp
// ❌ BAD: Timing-dependent, flaky
await Task.Delay(1000);
if (someCondition) { ... }

// ✅ GOOD: Deterministic, fast when condition met
await ActiveWaitHelpers.WaitUntilAsync(
    () => someCondition,
    timeout: TimeSpan.FromSeconds(5),
    description: "waiting for condition");
```

### 🚨 Critical: Debouncing Test Principle

**ConfigManager debounces by design (default 300ms).** Test final state correctness, not emission counts:

```csharp
// ✅ CORRECT: Test final state
behaviorSubject.OnNext("""{"Name": "Updated"}""");
Thread.Sleep(500); // Wait for debouncing
var config = manager.GetConfig<MyConfig>();
Assert.Equal("Updated", config.Name);

// ❌ WRONG: Assumes no debouncing
Assert.Equal(expectedEmissions, emissions.Count); // Flaky!
```

**Why:** Debouncing coalesces rapid changes → fewer emissions than changes. Final state is always deterministic.

---

## ✅ What Makes a Good Test

### 1. **Tests Behavior, Not Implementation**
```csharp
// ❌ BAD - Only tests instantiation
[Fact]
public void Test()
{
    var obj = new MyClass();
    Assert.NotNull(obj); // Always passes, no value
}

// ✅ GOOD - Tests actual behavior
[Fact]
public void Service_Returns_Correct_Configuration()
{
    var service = new MyService();
    var result = service.GetConfig();
    
    Assert.Equal(expectedValue, result.Property);
    Assert.True(result.IsValid);
}
```

### 2. **Test Name Matches Test Behavior**
```csharp
// ❌ BAD - Name claims "scoped" but doesn't verify it
[Fact]
public void Service_Is_Scoped()
{
    var instance = provider.GetService<MyService>();
    Assert.NotNull(instance); // Doesn't verify scoped behavior
}

// ✅ GOOD - Actually tests scoped behavior
[Fact]
public void Service_Is_Scoped()
{
    using var scope = provider.CreateScope();
    var instance1 = scope.ServiceProvider.GetService<MyService>();
    var instance2 = scope.ServiceProvider.GetService<MyService>();
    
    Assert.Same(instance1, instance2); // Verifies same instance
}
```

### 3. **Appropriate Assertions for Test Scope**

**For Provider Tests:** `Assert.NotNull(config)` is often sufficient
- Provider's job is to return config bytes
- Detailed value testing belongs in Core.Tests

**For Core Tests:** Assert actual values and behavior
- Verify configuration values with `Assert.Equal()`
- Verify object identity with `Assert.Same()` / `Assert.NotSame()`
- Verify behavior with `Assert.True()` / `Assert.False()`

**For DI Tests:** Verify service lifetime behavior
- Use `Assert.Same()` for singleton/scoped verification
- Use `Assert.NotSame()` for transient verification

### 4. **Use TestProviders in Core.Tests**

```csharp
// ✅ GOOD - Uses TestProvider (no I/O)
var rules = new List<ConfigRule>
{
    TestRules.StaticJson<MyConfig>(json),
    TestRules.Observable<MyConfig>(subject),
    TestRules.Failable<MyConfig>(shouldFail: true)
};

// ❌ BAD - Uses real I/O in Core.Tests
var rules = new List<ConfigRule>
{
    rules.For<MyConfig>().FromFile("config.json").Required() // ← NO!
};
```

---

## 🚫 Common Anti-Patterns

### 1. **Testing External Libraries**
```csharp
// ❌ BAD - Testing that File.WriteAllText works
[Fact]
public void FileSystem_Writes_File()
{
    File.WriteAllText("test.txt", "content");
    var content = File.ReadAllText("test.txt");
    Assert.Equal("content", content); // Testing .NET, not your code
}

// ✅ GOOD - Testing YOUR code that uses File I/O
[Fact]
public async Task FileProvider_Loads_Configuration_From_File()
{
    var provider = new FileSourceProvider(options);
    var config = await provider.FetchConfigurationBytesAsync(query);
    Assert.NotNull(config); // Testing your provider works
}
```

### 2. **Redundant Testing Across Layers**
```csharp
// ❌ BAD - Testing ConfigManager merging in Provider.Tests
[Fact]
public void FileProvider_Merges_Multiple_Files()
{
    var provider = new FileSourceProvider(options);
    // ... complex merging logic test ...
    // This belongs in Core.Tests, not Provider.Tests!
}

// ✅ GOOD - Provider.Tests only test provider behavior
[Fact]
public async Task FileProvider_Returns_File_Contents()
{
    var provider = new FileSourceProvider(options);
    var config = await provider.FetchConfigurationBytesAsync(query);
    Assert.NotNull(config);
}
```

### 3. **Constructor-Only Tests**
```csharp
// ❌ BAD - Provides no value
[Fact]
public void CanInstantiate()
{
    var obj = new MyClass();
    Assert.NotNull(obj); // DELETE THIS
}
```

---

## 📊 Test Quality Metrics

When reviewing tests, ask:

1. **Does this test validate behavior?** (Not just instantiation)
2. **Is it testing the right layer?** (Core vs Provider vs DI concerns)
3. **Would this test fail if there's a bug?** (Or does it always pass?)
4. **Is it testing YOUR code?** (Not third-party libraries)
5. **Is the test name accurate?** (Does it match what's actually tested?)

---

## 🛠️ Helper Utilities

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
var emissions = new List<T>();
await ObservableTestHelpers.WaitForValueAsync(
    observable, 
    emissions, 
    TimeSpan.FromSeconds(5), 
    "description");
```

---

## 🔧 When to Add Tests

### **Always Add Tests For:**
- New public API methods
- Bug fixes (regression tests)
- Complex logic or algorithms
- Error handling and edge cases
- Breaking changes (to document migration)

### **Consider NOT Adding Tests For:**
- Simple property getters/setters
- Pass-through methods to external libraries
- Obvious constructor behavior
- Trivial wrappers with no logic

### **Add Tests When Issues Are Detected:**
If you discover a bug in an external library:
1. Add a test that reproduces the issue
2. Document the external library bug
3. Implement a workaround in your code
4. The test proves your workaround works

---

## 🎯 Example: Testing a New Feature

**Scenario:** Adding a new `CacheProvider` that caches configuration in memory.

### Core.Tests (ConfigManager with CacheProvider)
```csharp
[Fact]
public async Task ConfigManager_Uses_Cached_Configuration()
{
    var fetchCount = 0;
    var testProvider = new TestProvider<MyConfig>(() => {
        fetchCount++;
        return new MyConfig { Value = fetchCount };
    });
    
    var rules = new List<ConfigRule> {
        TestRules.Cached(testProvider, cacheDuration: TimeSpan.FromMinutes(1))
    };
    
    var manager = ConfigManager.Create(c => c.WithConfiguration(rules));

    var config1 = manager.GetConfig<MyConfig>();
    var config2 = manager.GetConfig<MyConfig>();

    Assert.Equal(1, fetchCount); // Only fetched once (cached)
    Assert.Same(config1, config2);
}
```

### Provider.Tests (CacheProvider behavior)
```csharp
[Fact]
public async Task CacheProvider_Expires_After_Duration()
{
    var provider = new CacheProvider<MyConfig>(innerProvider, TimeSpan.FromMilliseconds(100));
    
    var config1 = await provider.FetchConfigurationBytesAsync(query);
    await Task.Delay(150); // Wait for cache expiration
    var config2 = await provider.FetchConfigurationBytesAsync(query);
    
    Assert.NotSame(config1, config2); // Cache expired, new fetch
}
```

---

## 🔄 CI/CD Integration

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

---

## 📚 Related Documentation

- [AGENTS.md](../AGENTS.md) - AI assistant guidance (includes testing philosophy)
- [CONTRIBUTING.md](../CONTRIBUTING.md) - Contribution guidelines
- **Core.Tests TestDocumentation/** - Test registry and performance benchmarks

---

**Version:** 1.0.0  
**Last Updated:** November 16, 2025
