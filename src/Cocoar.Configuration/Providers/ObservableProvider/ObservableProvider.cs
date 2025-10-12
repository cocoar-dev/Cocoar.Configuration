using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class ObservableProvider<T>(ObservableProviderOptions<T> options)
    : ConfigurationProvider<ObservableProviderOptions<T>, ObservableProviderQuery>(options)
{
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

public record ObservableProviderOptions<T>(IObservable<T> Observable) : IProviderConfiguration
{
    public string? GenerateProviderKey() => null;
}

public class ObservableProviderQuery : IProviderQuery
{
    public static readonly ObservableProviderQuery Default = new();
}


public static class ObservableRulesExtensions
{
    /// <summary>
    /// Creates an observable configuration rule from an observable stream.
    /// </summary>
    public static ProviderRuleBuilder<ObservableProvider<TValue>, ObservableProviderOptions<TValue>, ObservableProviderQuery>
        FromObservable<T, TValue>(this TypedRuleBuilder<T> builder, IObservable<TValue> observable)
    {
        return new(
            _ => new ObservableProviderOptions<TValue>(observable),
            _ => ObservableProviderQuery.Default,
            typeof(T)
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from a JSON string observable.
    /// </summary>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>
        FromObservable<T>(this TypedRuleBuilder<T> builder, IObservable<string> jsonObservable)
    {
        return new(
            _ => new ObservableProviderOptions<string>(jsonObservable),
            _ => ObservableProviderQuery.Default,
            typeof(T)
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from an initial JSON string.
    /// </summary>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>
        FromObservable<T>(this TypedRuleBuilder<T> builder, string initialJsonString)
    {
        using var document = JsonDocument.Parse(initialJsonString);

        var subject = new BehaviorSubject<string>(initialJsonString);

        return new(
            _ => new ObservableProviderOptions<string>(subject),
            _ => ObservableProviderQuery.Default,
            typeof(T)
        );
    }
}
