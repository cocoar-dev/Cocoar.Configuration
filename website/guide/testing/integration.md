# Integration Testing

## The AsyncLocal Gap

`AsyncLocal<T>` flows through `async`/`await` within the same async context. However, xUnit creates **separate async contexts** for fixture setup and test methods. Configuration set in `InitializeAsync()` is **not visible** in test methods.

The solution: build the context once, apply it in each test's constructor.

## Fixture Pattern

```csharp
public class IntegrationTestFixture
{
    public TestConfigurationContext TestContext { get; } =
        TestConfigurationContext.Replace(rule => [
            rule.For<DbConfig>().FromStatic(_ => new DbConfig
            {
                ConnectionString = "Server=localhost;Database=integration_test"
            }),
            rule.For<AppSettings>().FromStatic(_ => new AppSettings
            {
                LogLevel = "Debug"
            })
        ]);
}

public class OrderTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public OrderTests(IntegrationTestFixture fixture)
    {
        // Bridge the async context gap
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task PlaceOrder_WithValidConfig_Succeeds()
    {
        // Configuration is visible here
        var manager = ConfigManager.Create(c => c.UseConfiguration(rules => [
            rules.For<DbConfig>().FromFile("db.json")
        ]));

        // Uses test values, not real db.json
        var config = manager.GetConfig<DbConfig>();
        Assert.Equal("Server=localhost;Database=integration_test", config!.ConnectionString);
    }
}
```

## ASP.NET Core Integration Tests <Badge type="info" text="ADV" />

Use the same fixture pattern with `WebApplicationFactory`:

```csharp
public class ApiTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    public ApiTests(IntegrationTestFixture fixture)
    {
        CocoarTestConfiguration.Apply(fixture.TestContext);
    }

    public void Dispose() => CocoarTestConfiguration.Clear();

    [Fact]
    public async Task GetSettings_ReturnsTestValues()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/settings");
        response.EnsureSuccessStatusCode();

        // App inside WebApplicationFactory uses test configuration
    }
}
```

The `AsyncLocal` flows from the test method into the `WebApplicationFactory` startup, so `ConfigManager` inside the app sees the test configuration.

## Per-Test Overrides <Badge type="info" text="ADV" />

For tests that need different configuration from the fixture:

```csharp
[Fact]
public async Task HandlesHighRetryCount()
{
    // Override just for this test
    using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
        rule.For<AppSettings>().FromStatic(_ => new AppSettings { MaxRetries = 100 })
    ]);

    // This test sees MaxRetries = 100
    // Other parallel tests are unaffected
}
```

## Secrets in Tests

```csharp
public class SecretsTestFixture
{
    public TestConfigurationContext TestContext { get; }

    public SecretsTestFixture()
    {
        TestContext = new TestOverrideBuilder()
            .ReplaceConfiguration(rule => [
                rule.For<ApiConfig>().FromStatic(_ => new ApiConfig
                {
                    ApiKey = "test-key-plaintext"
                })
            ])
            .ReplaceSecretsSetup(s => s.AllowPlaintext())
            .Build();
    }
}
```

## Avoiding Timing Issues

When testing reactive configuration updates, use active polling instead of `Task.Delay()`:

```csharp
// ❌ Fragile — depends on timing
await Task.Delay(500);
Assert.Equal(expected, config.Value);

// ✓ Deterministic — polls until condition is met
await ActiveWaitHelpers.WaitForValueAsync(
    () => reactiveConfig.CurrentValue.MaxRetries,
    expectedValue: 42,
    timeout: TimeSpan.FromSeconds(5));
```

Active waiting with short poll intervals (50ms default) gives you fast tests that don't flake under load.

## Test Provider <Badge type="info" text="ADV" />

For tests that simulate provider failures:

```csharp
var provider = FailableProvider.FailAfterNCalls(
    json: """{"MaxRetries": 3}""",
    callsBeforeFailure: 2);

var manager = ConfigManager.Create(c => c.UseConfiguration(rules => [
    rules.For<AppSettings>().FromProvider(provider)
]));

// First 2 calls succeed, then provider starts failing
// Health transitions from Healthy → Degraded (if optional) or rolls back (if required)
```
