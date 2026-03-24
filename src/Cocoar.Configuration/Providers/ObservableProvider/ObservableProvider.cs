using System.Text.Json;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class ObservableProvider<T>(ObservableProviderOptions<T> options)
    : ConfigurationProvider<ObservableProviderOptions<T>, ObservableProviderQuery>(options)
{
    public override async Task<byte[]> FetchConfigurationBytesAsync(ObservableProviderQuery query, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<byte[]>();
        using var ctr = ct.Register(() => tcs.TrySetCanceled(ct));
        IDisposable? sub = null;
        sub = ProviderOptions.Observable.Subscribe(
            value => { tcs.TrySetResult(ConvertToBytes(value)); sub?.Dispose(); },
            ex => { tcs.TrySetException(ex); sub?.Dispose(); });
        return await tcs.Task.ConfigureAwait(false);
    }

    public override IObservable<byte[]> ChangesAsBytes(ObservableProviderQuery query)
    {
        return ProviderOptions.Observable.Select(ConvertToBytes);
    }

    private byte[] ConvertToBytes(T value)
    {
        if (typeof(T) == typeof(string) && value is string jsonString)
        {
            return System.Text.Encoding.UTF8.GetBytes(jsonString);
        }

        return JsonSerializer.SerializeToUtf8Bytes(value);
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
        FromObservable<T, TValue>(this TypedProviderBuilder<T> builder, IObservable<TValue> observable)
        where T : class
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
        FromObservable<T>(this TypedProviderBuilder<T> builder, IObservable<string> jsonObservable)
        where T : class
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
        FromObservable<T>(this TypedProviderBuilder<T> builder, string initialJsonString)
        where T : class
    {
        using var document = JsonDocument.Parse(initialJsonString);

        var subject = new SimpleBehaviorSubject<string>(initialJsonString);

        return new(
            _ => new ObservableProviderOptions<string>(subject),
            _ => ObservableProviderQuery.Default,
            typeof(T)
        );
    }
}
