using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;

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

    /// <summary>
    /// Helper method to create a configuration rule for testing purposes.
    /// </summary>
    public static ConfigRule CreateRule<TConfig>(IObservable<TConfig> observable, bool required = false, Func<bool>? useWhen = null)
    {
        return ConfigRule.Create<ObservableProvider<TConfig>, ObservableProviderOptions<TConfig>, ObservableProviderQuery>(
            _ => new ObservableProviderOptions<TConfig>(observable),
            _ => ObservableProviderQuery.Default,
            typeof(TConfig),
            new ConfigRuleOptions(Required: required, UseWhen: useWhen)
        );
    }

    /// <summary>
    /// Helper method to create a configuration rule from JSON string observable for testing purposes.
    /// </summary>
    public static ConfigRule CreateRule<TConfig>(IObservable<string> jsonObservable, bool required = false, Func<bool>? useWhen = null)
    {
        return ConfigRule.Create<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>(
            _ => new ObservableProviderOptions<string>(jsonObservable),
            _ => ObservableProviderQuery.Default,
            typeof(TConfig),
            new ConfigRuleOptions(Required: required, UseWhen: useWhen)
        );
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
    public static ProviderRuleBuilder<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery>
        Observable<T>(this RulesBuilder builder, IObservable<T> observable)
    {
        return builder.FromProvider<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery>(
            _ => new(observable),
            _ => ObservableProviderQuery.Default
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from a JSON string observable.
    /// </summary>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>
        Observable(this RulesBuilder builder, IObservable<string> jsonObservable)
    {
        return builder.FromProvider<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>(
            _ => new(jsonObservable),
            _ => ObservableProviderQuery.Default
        );
    }

    /// <summary>
    /// Creates an observable configuration rule from an initial JSON string.
    /// </summary>
    public static ProviderRuleBuilder<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>
        Observable(this RulesBuilder builder, string initialJsonString)
    {

        using var document = JsonDocument.Parse(initialJsonString);

        var subject = new BehaviorSubject<string>(initialJsonString);

        return builder.FromProvider<ObservableProvider<string>, ObservableProviderOptions<string>, ObservableProviderQuery>(
            _ => new(subject),
            _ => ObservableProviderQuery.Default
        );
    }
}
