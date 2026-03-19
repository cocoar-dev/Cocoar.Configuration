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
        return Task.FromResult(_cachedBytes);
    }

    public override IObservable<byte[]> ChangesAsBytes(StaticJsonProviderQueryOptions queryOptions)
        => ObservableHelpers.Empty<byte[]>();
}
