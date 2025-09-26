using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class ObservableProvider<T> : ConfigurationProvider<ObservableProviderOptions<T>, ObservableProviderQuery>
{
    public ObservableProvider(ObservableProviderOptions<T> options) : base(options)
    {
    }

    public override Task<JsonElement> FetchConfigurationAsync(ObservableProviderQuery query, CancellationToken ct = default)
    {
        var observable = ProviderOptions.Observable.Select(value => 
        {
            if (typeof(T) == typeof(string) && value is string jsonString)
            {
                using var document = JsonDocument.Parse(jsonString);
                return document.RootElement.Clone();
            }
                
            var json = JsonSerializer.Serialize(value);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        });
            
        return observable.FirstAsync().ToTask(ct);
    }

    public override IObservable<JsonElement> Changes(ObservableProviderQuery query)
    {
        return ProviderOptions.Observable.Select(value => 
        {
            if (typeof(T) == typeof(string) && value is string jsonString)
            {
                using var document = JsonDocument.Parse(jsonString);
                return document.RootElement.Clone();
            }
                
            var json = JsonSerializer.Serialize(value);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        });
    }

}

/// <summary>
/// Provider options that accept an IObservable
/// </summary>
public class ObservableProviderOptions<T> : IProviderConfiguration
{
    public IObservable<T> Observable { get; }

    public ObservableProviderOptions(IObservable<T> observable)
    {
        Observable = observable;
    }

    public string? GenerateProviderKey() => null;
}

/// <summary>
/// Simple query for observable provider
/// </summary>
public class ObservableProviderQuery : IProviderQuery
{
    public static readonly ObservableProviderQuery Default = new();
}

/// <summary>
/// Fluent extensions for ObservableProvider - your exact API!
/// Rule.From.Observable(behaviorSubject).For&lt;TestConfig&gt;()
/// Rule.From.Observable(jsonString).For&lt;TestConfig&gt;()  
/// </summary>
public static class ObservableRulesExtensions
{
    /// <summary>
    /// Creates an observable configuration rule from an IObservable&lt;T&gt;.
    /// Perfect for testing - just pass in a BehaviorSubject and emit values as needed!
    /// </summary>
    /// <typeparam name="T">The type of objects emitted by the observable.</typeparam>
    /// <param name="dsl">The rule DSL.</param>
    /// <param name="observable">The observable that emits configuration values.</param>
    /// <returns>A provider rule builder for further configuration.</returns>
    public static ProviderRuleBuilder<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery> 
        Observable<T>(this Rule.Dsl dsl, IObservable<T> observable)
    {
        return Rule.FromProvider<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery>(
            _ => new(observable),
            _ => ObservableProviderQuery.Default
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from an IObservable&lt;string&gt; containing JSON.
    /// Perfect for testing with JSON strings that change over time!
    /// Rule.From.Observable(jsonObservable).For&lt;TestConfig&gt;()
    /// </summary>
    /// <param name="dsl">The rule DSL.</param>
    /// <param name="jsonObservable">The observable that emits JSON strings.</param>
    /// <returns>A provider rule builder for further configuration.</returns>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery> 
        Observable(this Rule.Dsl dsl, IObservable<string> jsonObservable)
    {
        return Rule.FromProvider<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>(
            _ => new(jsonObservable),
            _ => ObservableProviderQuery.Default
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from a single JSON string using BehaviorSubject.
    /// Perfect for testing - creates a BehaviorSubject&lt;string&gt; with the initial JSON value!
    /// Rule.From.Observable(initialJson).For&lt;TestConfig&gt;()
    /// </summary>
    /// <param name="dsl">The rule DSL.</param>
    /// <param name="initialJsonString">The initial JSON string value.</param>
    /// <returns>A provider rule builder for further configuration.</returns>
    /// <exception cref="JsonException">Thrown when the JSON string is invalid.</exception>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery> 
        Observable(this Rule.Dsl dsl, string initialJsonString)
    {
        // Validate JSON by parsing it
        using var document = JsonDocument.Parse(initialJsonString);
            
        // Create a BehaviorSubject with the initial JSON string
        var subject = new BehaviorSubject<string>(initialJsonString);
            
        return Rule.FromProvider<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>(
            _ => new(subject),
            _ => ObservableProviderQuery.Default
        );
    }
}
