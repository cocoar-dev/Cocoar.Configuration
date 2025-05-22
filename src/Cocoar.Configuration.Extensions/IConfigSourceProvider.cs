using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public interface IConfigSourceProvider
{
    // string SourceKind { get; }
    // string SourceIdentifier { get; }
    Task<JsonElement?> GetValueAsync(string? part = null, CancellationToken ct = default);
    IObservable<ConfigChangeNotification> Changes(string? part = null);
}

public abstract class ConfigSourceProvider
{
    // public abstract string SourceKind { get; }
    // public abstract string SourceIdentifier { get; }

    public abstract Task<JsonElement?> GetValueAsync(string? part = null, CancellationToken ct = default);
    public abstract IObservable<ConfigChangeNotification> Changes(string? part = null);
}

public interface IConfigSourceProviderConfig
{
    string CalculateKey();
}

public abstract class ConfigSourceProvider<TOptions>(TOptions options): ConfigSourceProvider
    where TOptions : IConfigSourceProviderConfig
{
    protected TOptions ProviderOptions { get; } = options;

}