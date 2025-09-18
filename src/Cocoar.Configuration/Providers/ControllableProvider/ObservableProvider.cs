using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.Providers.ObservableProvider
{
    /// <summary>
    /// A configuration provider that directly accepts an IObservable for complete control.
    /// Perfect for testing - just pass in a BehaviorSubject and emit values as needed!
    /// </summary>
    public sealed class ObservableProvider<T> : ConfigurationProvider<ObservableProviderOptions<T>, ObservableProviderQuery>
    {
        public ObservableProvider(ObservableProviderOptions<T> options) : base(options)
        {
        }

        public override Task<JsonElement> FetchConfigurationAsync(ObservableProviderQuery query, CancellationToken ct = default)
        {
            // For observable provider, we need to get the current value
            var observable = ProviderOptions.Observable.Select(value => 
            {
                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<JsonElement>(json);
            });
            
            return observable.FirstAsync().ToTask(ct);
        }

        public override IObservable<JsonElement> Changes(ObservableProviderQuery query)
        {
            return ProviderOptions.Observable.Select(value => 
            {
                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<JsonElement>(json);
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
    }

    /// <summary>
    /// Simple query for observable provider
    /// </summary>
    public class ObservableProviderQuery : IProviderQuery
    {
        public static readonly ObservableProviderQuery Default = new();
    }
}

namespace Cocoar.Configuration.Providers.ObservableProvider
{
    /// <summary>
    /// Fluent extensions for ObservableProvider - your exact API!
    /// Rule.From.Controllable(behaviorSubject).For&lt;TestConfig&gt;()
    /// </summary>
    public static class RulesExtensions
    {
        public static ProviderRuleBuilder<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery> 
            Controllable<T>(this Rule.Dsl _, IObservable<T> observable)
        {
            return Rule.FromProvider<ObservableProvider<T>, ObservableProviderOptions<T>, ObservableProviderQuery>(
                _ => new ObservableProviderOptions<T>(observable),
                _ => ObservableProviderQuery.Default
            );
        }
    }
}