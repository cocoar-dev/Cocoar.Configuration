# Creating Custom Configuration Providers

This guide explains how to create custom configuration providers for Cocoar.Configuration.

## Provider Architecture Overview

Cocoar providers follow a simple contract-based architecture:

- **Abstractions** live under `Providers/Abstractions` and define the base types used by all providers
- **Implementations** live under `Providers/<Name>/` and depend only on the abstractions
- **Pooling/reuse**: `ProviderRegistry` shares instances by provider type + instance options `CalculateKey()`
- **Change semantics**: `Changes()` must not emit an initial value; initial compute happens via `GetValueAsync` during `ConfigManager.Initialize()`

## Creating a Custom Provider

### 1. Define Your Options Classes

Create classes that implement `ISourceProviderInstanceOptions` and `ISourceProviderQueryOptions`:

```csharp
using Cocoar.Configuration.Providers.Abstractions;

// Instance options: shared across queries for the same provider instance
public sealed class MyProviderOptions : ISourceProviderInstanceOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    // Used for provider instance pooling - must be deterministic
    public string CalculateKey() => $"MyProvider:{ConnectionString}:{Timeout}";
}

// Query options: specific to each configuration rule
public sealed class MyProviderQueryOptions : ISourceProviderQueryOptions
{
    public string TableName { get; set; } = string.Empty;
    public string? FilterExpression { get; set; }
    
    public string CalculateKey() => $"Table:{TableName}:Filter:{FilterExpression}";
}
```

### 2. Implement the Provider

Extend `ConfigSourceProvider<TInstanceOptions, TQueryOptions>`:

```csharp
using Cocoar.Configuration.Providers.Abstractions;
using System.Text.Json;

public sealed class MyProvider : ConfigSourceProvider<MyProviderOptions, MyProviderQueryOptions>
{
    public MyProvider(MyProviderOptions instanceOptions) : base(instanceOptions) { }

    public override async Task<JsonElement?> GetValueAsync(MyProviderQueryOptions queryOptions, CancellationToken cancellationToken = default)
    {
        // Fetch data based on instance and query options
        var data = await FetchDataFromSource(queryOptions.TableName, queryOptions.FilterExpression);
        
        // Return as JsonElement (or null if no data)
        if (data == null) return null;
        
        var json = JsonSerializer.Serialize(data);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public override IObservable<ConfigSourceProviderChange> Changes(MyProviderQueryOptions queryOptions)
    {
        // Return an observable that emits when the data changes
        // Must NOT emit initial value - only changes
        return Observable.Create<ConfigSourceProviderChange>(observer =>
        {
            // Set up change monitoring (database triggers, polling, etc.)
            var subscription = MonitorChanges(queryOptions.TableName, () =>
            {
                observer.OnNext(ConfigSourceProviderChange.Instance);
            });
            
            return subscription; // Return disposable to clean up
        });
    }
    
    private async Task<object?> FetchDataFromSource(string tableName, string? filter)
    {
        // Your implementation here
        // Connect using InstanceOptions.ConnectionString, etc.
        throw new NotImplementedException();
    }
    
    private IDisposable MonitorChanges(string tableName, Action onChanged)
    {
        // Your change monitoring implementation
        throw new NotImplementedException();
    }
}
```

### 3. Create Fluent Extensions (Optional)

Add convenience methods for easier rule creation:

```csharp
using Cocoar.Configuration.Fluent;

namespace MyCompany.Configuration.MyProvider;

public static class MyProviderRulesExtensions
{
    public static ProviderRuleBuilder<MyProvider, MyProviderOptions, MyProviderQueryOptions> FromMyProvider(
        this Rules.Dsl _,
        string connectionString,
        string tableName,
        TimeSpan? timeout = null)
        => Rules.FromProvider<MyProvider, MyProviderOptions, MyProviderQueryOptions>(
            instance: _ => new MyProviderOptions 
            { 
                ConnectionString = connectionString, 
                Timeout = timeout ?? TimeSpan.FromSeconds(30) 
            },
            query: _ => new MyProviderQueryOptions { TableName = tableName });
}
```

## Important Implementation Guidelines

### CalculateKey() Requirements
- Must be **deterministic** and **stable** across application runs
- Used for provider instance pooling - same key = same instance
- Include all properties that affect provider behavior
- Keep keys reasonably short but unique

### Change Semantics
- `Changes()` observable must **never emit initial values**
- Initial data loading happens through `GetValueAsync` during initialization
- Only emit when data actually changes to avoid unnecessary recomputes
- Emit `ConfigSourceProviderChange.Instance` (singleton) to trigger recomputation

### Nesting Separators
For environment variable providers, support these nesting separators:
- **`__`** (double underscore) for object nesting
- **`:`** for object nesting  
- **Single `_`** is treated as literal (not a separator)
- Leading `_` or `:` after a prefix should be trimmed for convenience

### Error Handling
- Throw exceptions in `GetValueAsync` for required rules that should fail
- Swallow transient IO errors in the `Changes()` stream to keep it alive
- Use appropriate cancellation token support

## Testing Your Provider

```csharp
[Test]
public async Task MyProvider_ShouldReturnExpectedConfig()
{
    var instanceOptions = new MyProviderOptions { ConnectionString = "test" };
    var queryOptions = new MyProviderQueryOptions { TableName = "config" };
    
    var provider = new MyProvider(instanceOptions);
    var result = await provider.GetValueAsync(queryOptions);
    
    Assert.That(result, Is.Not.Null);
    // Assert specific JSON structure
}

[Test] 
public void MyProvider_CalculateKey_ShouldBeStable()
{
    var options = new MyProviderOptions { ConnectionString = "test", Timeout = TimeSpan.FromSeconds(30) };
    
    var key1 = options.CalculateKey();
    var key2 = options.CalculateKey();
    
    Assert.That(key1, Is.EqualTo(key2));
}
```

## Integration Example

```csharp
using Cocoar.Configuration.Fluent;
using MyCompany.Configuration.MyProvider;

var rules = new[]
{
    // Using the fluent extension
    Rules.Using
        .FromMyProvider("Server=localhost;Database=config", "app_settings")
        .For<MySettings>()
        .Required()
        .Build(),
        
    // Or using generic provider syntax
    Rules.FromProvider<MyProvider, MyProviderOptions, MyProviderQueryOptions>(
        instance: _ => new MyProviderOptions { ConnectionString = "..." },
        query: _ => new MyProviderQueryOptions { TableName = "settings" })
        .For<MySettings>()
        .Build()
};

services.AddCocoarConfiguration(rules);
```

## Notes for Project Extraction

If moving providers to separate projects:
- Reference `Cocoar.Configuration.Abstractions` only
- Keep `CalculateKey()` implementations stable for pooling compatibility  
- Maintain consistent wrapper/member behaviors for nested value shaping
- The Microsoft IConfigurationSource adapter lives in `Cocoar.Configuration.MicrosoftAdapter` as an example
