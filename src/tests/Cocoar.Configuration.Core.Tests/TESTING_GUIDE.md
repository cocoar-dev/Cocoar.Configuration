# Testing Guide - Cocoar Configuration Core Tests

This guide shows you how to use the bulletproof testing infrastructure for Cocoar Configuration providers.

> **📋 For All Future Development:** This guide establishes the testing standards for all new tests, providers, and contributions to the project. Follow these patterns to ensure reliability and consistency.

## Quick Start

```bash
# Navigate to the Core Tests project
cd src/tests/Cocoar.Configuration.Core.Tests

# Run all tests
dotnet test

# Run only fast unit tests (good for development)
dotnet test --filter "Type=Unit"
```

## What This Project Provides

The Core Tests project establishes a **bulletproof testing foundation** for the most reliable configuration providers. It proves that StaticJsonProvider and ObservableProvider are safe and reliable for production use.

**Key Features:**
- ✅ **Zero timing dependencies** - Uses active waiting patterns instead of fixed delays
- ✅ **Deterministic providers only** - No file I/O, network calls, or external dependencies  
- ✅ **Trait-based filtering** - Run specific test categories as needed
- ✅ **Performance baselines** - Detect regressions automatically

### Strategic Testing Philosophy

**Why stress test "simple" providers like ObservableProvider?**

While ObservableProvider wraps System.Reactive (which is well-tested), our stress tests validate crucial aspects:

- **Provider wrapper logic** - Serialization, error handling, subscription management under load
- **Integration points** - How our provider interacts with underlying observables during rapid changes
- **Resource management** - Memory leaks, disposal, thread safety under extreme conditions
- **Performance characteristics** - Real-world load scenarios (100+ concurrent operations)

**The ConfigManager Challenge:**

ConfigManager uses complex debounce and cancellation logic:
- When 100+ recompute signals hit ConfigManager, it debounces them
- If new signals arrive during debouncing, it cancels and restarts
- This creates race conditions and rapid provider stress

**Why This Layered Approach Works:**

```
┌─────────────────────────────────────┐
│     ConfigManager Integration       │  ← Future: Test complex logic
│   (debounce, cancel, recompute)     │    with bulletproof providers
├─────────────────────────────────────┤  
│    Provider Stress Tests ✅         │  ← Current: Prove providers
│  (race conditions, heavy load)      │    can handle ANY stress
├─────────────────────────────────────┤
│    Provider Unit Tests ✅           │  ← Foundation: Basic functionality
│   (serialization, basic ops)        │    works correctly
└─────────────────────────────────────┘
```

**Benefits:**
1. **Failure Isolation** - When integration tests fail, we know it's NOT the providers
2. **Confidence** - Providers proven to handle 1000+ rapid changes
3. **Realistic Testing** - Integration tests use REAL stress-tested components
4. **Debugging** - Failures must be in higher-level logic, not provider layer

## Test Categories & Filtering

Tests are organized using `[Trait]` attributes for selective execution:

### By Test Type
```bash
dotnet test --filter "Type=Unit"         # Fast basic functionality tests
dotnet test --filter "Type=Performance"  # Performance benchmarks
dotnet test --filter "Type=Concurrency"  # Thread safety tests
dotnet test --filter "Type=Stress"       # Load/stress tests
```

### By Provider
```bash
dotnet test --filter "Provider=StaticJsonProvider"    # JSON provider tests
dotnet test --filter "Provider=ObservableProvider"    # Observable provider tests
```

### Combined Filtering
```bash
# Unit tests for ObservableProvider only
dotnet test --filter "Type=Unit&Provider=ObservableProvider"

# Everything except stress tests (faster feedback)
dotnet test --filter "Type!=Stress"
```

## CI/CD Integration

### Pull Request Checks (Fast)
```bash
# Quick feedback for PRs - runs in ~1 second
dotnet test --filter "Type=Unit" --logger:trx
```

### Full Test Suite (Complete)
```bash
# Complete validation including stress tests - runs in ~5 seconds
dotnet test --logger:trx --collect:"XPlat Code Coverage"
```

### Performance Monitoring
```bash
# Check for performance regressions
dotnet test --filter "Type=Performance" --logger:trx
```

## Writing New Tests

When adding tests to this project, follow these principles:

### 1. Use Active Waiting (Not Fixed Delays)
```csharp
// ❌ BAD: Timing-dependent
await Task.Delay(1000);

// ✅ GOOD: Active waiting
await ActiveWaitHelpers.WaitUntilAsync(
    () => someCondition,
    timeout: TimeSpan.FromSeconds(5),
    description: "condition description");
```

### 2. Add Appropriate Traits
```csharp
[Fact]
[Trait("Type", "Unit")]  // Unit, Performance, Concurrency, or Stress
[Trait("Provider", "YourProvider")]
public async Task YourTest_Scenario_ExpectedResult()
{
    // Test implementation
}
```

### 3. Keep Tests Deterministic
- ✅ No file I/O
- ✅ No network calls  
- ✅ No timing dependencies
- ✅ Controlled test data
- ✅ Isolated from other tests

### 🚨 4. **CRITICAL: Debouncing Test Principle** 🚨

**ConfigManager has debouncing by design** (default 300ms). This means:

#### **✅ DO: Test Final State Correctness**
```csharp
// ✅ CORRECT: Focus on final configuration correctness
behaviorSubject.OnNext("""{"Name": "UpdatedValue"}""");

// Wait for debouncing to settle
Thread.Sleep(500); 

// Assert final state is correct
var config = configManager.GetConfig<MyConfig>();
Assert.Equal("UpdatedValue", config.Name);
```

#### **❌ DON'T: Assert Specific Emission Counts**
```csharp
// ❌ WRONG: This will fail randomly due to debouncing
Assert.True(emissions.Count > initialCount, "Should have received update emission");

// ❌ WRONG: This assumes no debouncing
Assert.Equal(expectedEmissions, emissions.Count);
```

#### **Why This Matters**
- **Debouncing coalesces rapid changes** → fewer emissions than changes
- **Timing is non-deterministic** → emission counts vary based on system load  
- **Final state is deterministic** → configuration correctness is guaranteed
- **Tests should align with design** → debouncing is a feature, not a bug

#### **Correct Testing Patterns**
```csharp
// ✅ Test that changes propagate to final state
var latestConfig = emissions.Last();
Assert.Equal(expectedFinalValue, latestConfig.Property);

// ✅ Test that reactive config matches snapshot config  
var snapshot = configManager.GetConfig<MyConfig>();
Assert.Equal(latestConfig.Property, snapshot.Property);

// ✅ Test debouncing effectiveness (fewer emissions than changes)
var changeCount = 100;
// ... make 100 rapid changes ...
Assert.True(emissions.Count < changeCount, "Debouncing should reduce emissions");
```

#### **Documentation References**
- See `ConfigManagerIsolationTests` for proper debouncing test examples
- See `RapidChangeHandlingTests` for emission count validation patterns
- Architecture: debouncing is fundamental to ConfigManager performance

## Helper Utilities

The project provides bulletproof testing utilities:

### ActiveWaitHelpers
```csharp
// Wait for a condition to become true
await ActiveWaitHelpers.WaitUntilAsync(() => condition, TimeSpan.FromSeconds(5), "description");

// Wait for a specific value
var result = await ActiveWaitHelpers.WaitForValueAsync(() => getValue(), TimeSpan.FromSeconds(5), "description");
```

### ObservableTestHelpers
```csharp
// Wait for observable emissions
var emissions = new List<T>();
await ObservableTestHelpers.WaitForValueAsync(observable, emissions, TimeSpan.FromSeconds(5), "description");
```

## Documentation References

For detailed information, see the TestDocumentation folder:
- **`TEST_REGISTRY.md`** - Complete inventory of all tests with descriptions
- **`PERFORMANCE_BENCHMARKS.md`** - Performance targets and regression thresholds

## Extending the Foundation

This project establishes the **testing standards** for all Cocoar Configuration development:

### For Internal Development Team
1. **Follow existing patterns** in `Providers/` folder for any new providers
2. **Use the same trait categories** to maintain consistency across all tests  
3. **Apply bulletproof principles** for all new test scenarios
4. **Update TestDocumentation** when adding significant new functionality
5. **Maintain performance baselines** as the codebase evolves

### For External Contributors
When contributing tests or new providers:
1. **Review this guide first** - it defines our testing standards
2. **Match existing test structure** - look at StaticJsonProvider/ObservableProvider examples
3. **Include appropriate traits** - your tests should be filterable like existing ones
4. **Prove deterministic behavior** - no timing dependencies or external I/O
5. **Add documentation** - update TEST_REGISTRY.md with new test descriptions

### For Provider Authors
If you're creating custom providers for Cocoar Configuration:
1. **Use this project as your testing template** - copy the patterns we've established
2. **Implement the same trait system** - make your tests filterable by CI systems
3. **Follow bulletproof principles** - deterministic tests are reliable tests
4. **Provide performance baselines** - help users understand your provider's characteristics
5. **Document your test coverage** - follow our TestDocumentation structure

### Adopting This Guide
As the project evolves:
- **Update this guide** when introducing new testing patterns
- **Extend trait categories** if new test types are needed (e.g., "Security", "Migration")
- **Evolve helper utilities** in TestUtilities/ as common patterns emerge
- **Keep examples current** with actual command outputs and real scenarios

## Integration with Main Test Suite

This Core Tests project works alongside the main `Cocoar.Configuration.Tests`:

- **Core Tests**: Prove providers are bulletproof (deterministic, fast)
- **Main Tests**: Integration scenarios (file I/O, complex workflows)

Both should run in CI, but Core Tests provide rapid feedback while Main Tests ensure comprehensive coverage.