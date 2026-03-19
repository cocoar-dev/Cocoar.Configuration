# Test Overrides

`CocoarTestConfiguration` lets you replace or extend configuration in tests without touching real files, environment variables, or HTTP endpoints. It uses `AsyncLocal<T>` for isolation — each test gets its own configuration context, parallel-safe.

## Replace vs Append

### ReplaceConfiguration

Skips all original rules. Only your test rules execute:

```csharp
using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [
    rule.For<DbConfig>().FromStatic(_ => new DbConfig
    {
        ConnectionString = "Server=localhost;Database=test"
    })
]);

// ConfigManager now uses only the test rule
```

Use this when:
- Original providers would fail in the test environment (missing files, unreachable URLs)
- You want complete isolation from real configuration

### AppendConfiguration

Original rules execute first, then your test rules overlay on top (last-write-wins merge):

```csharp
using var _ = CocoarTestConfiguration.AppendConfiguration(rule => [
    rule.For<AppSettings>().FromStatic(_ => new AppSettings
    {
        MaxRetries = 999
    })
]);

// Original AppSettings rules run first, then test values merge over them
```

Use this when:
- You only need to override specific values
- You want the rest of the configuration to behave normally

## Secrets Override

Secrets setup is overridden independently from rules. You can combine it with either mode:

```csharp
// Replace rules + allow plaintext secrets
using var _ = CocoarTestConfiguration
    .ReplaceConfiguration(rule => [...])
    .ReplaceSecretsSetup(s => s.AllowPlaintext());

// Only override secrets, keep original rules
using var _ = CocoarTestConfiguration
    .ReplaceSecretsSetup(s => s.AllowPlaintext());
```

`AllowPlaintext()` is the most common test override — it skips encryption so you can use plain JSON values in test configuration.

## Setup Override <Badge type="info" text="ADV" />

You can also override the setup (DI registration customization) alongside rules:

```csharp
using var _ = CocoarTestConfiguration.ReplaceConfiguration(
    rules: rule => [
        rule.For<AppSettings>().FromStatic(_ => new AppSettings { LogLevel = "Debug" })
    ],
    setup: setup => [
        setup.ConcreteType<AppSettings>().AsSingleton()
    ]);
```

## Disposal

The `using` pattern ensures cleanup. When the scope disposes, `CocoarTestConfiguration.Clear()` is called and the test context is removed from the `AsyncLocal`:

```csharp
[Fact]
public async Task MyTest()
{
    using var _ = CocoarTestConfiguration.ReplaceConfiguration(rule => [...]);

    // Test code — configuration is overridden here
    // ...

} // Automatically cleared, even on exception
```

## How It Works

1. `CocoarTestConfiguration` stores a `TestConfigurationContext` in an `AsyncLocal<T>`
2. The context flows through `async`/`await` chains automatically
3. When `ConfigManager` initializes, it checks `CocoarTestConfiguration.IsActive`
4. If active, it uses the test rules (Replace or Append mode) instead of or in addition to the configured rules
5. Each test's `AsyncLocal` is isolated — parallel tests don't interfere
