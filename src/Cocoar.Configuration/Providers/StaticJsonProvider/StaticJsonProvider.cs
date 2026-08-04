using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class StaticJsonProvider(StaticJsonProviderOptions options)
    : ConfigurationProvider<StaticJsonProviderOptions, StaticJsonProviderQueryOptions>(options)
{
    private readonly byte[] _cachedBytes = SerializeToBytes(options.Value);

    private static byte[] SerializeToBytes(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Undefined
            ? "{}"u8.ToArray()
            : JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public override Task<byte[]> FetchConfigurationBytesAsync(StaticJsonProviderQueryOptions query,
        CancellationToken ct = default)
    {
        // Callers may zero the array they receive, and one provider instance is shared by every
        // rule with identical payload, so the cached buffer must never leave this object.
        return Task.FromResult((byte[])_cachedBytes.Clone());
    }

    public override IObservable<byte[]> ChangesAsBytes(StaticJsonProviderQueryOptions queryOptions)
        => ObservableHelpers.Empty<byte[]>();
}
