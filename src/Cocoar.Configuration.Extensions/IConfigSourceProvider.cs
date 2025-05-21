using System.Text.Json;

namespace Cocoar.Configuration.Extensions;

public interface IConfigSourceProvider
{
    string SourceKind { get; }
    string SourceIdentifier { get; }
    Task<JsonElement?> GetValueAsync(string? part = null, CancellationToken ct = default);
    IObservable<ConfigChangeNotification> Changes(string? part = null);
}