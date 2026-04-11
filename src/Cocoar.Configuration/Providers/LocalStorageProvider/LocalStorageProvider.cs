using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Provider that reads from a <see cref="LocalStorageStore"/>.
/// The store is shared state owned by the closure in <c>FromLocalStorage()</c> —
/// the provider does NOT own or dispose it.
/// </summary>
public sealed class LocalStorageProvider(LocalStorageProviderOptions options)
    : ConfigurationProvider<LocalStorageProviderOptions, LocalStorageProviderQueryOptions>(options)
{
    public override async Task<byte[]> FetchConfigurationBytesAsync(
        LocalStorageProviderQueryOptions query, CancellationToken ct = default)
    {
        return await ProviderOptions.Store.ReadBytesAsync(ct).ConfigureAwait(false);
    }

    public override IObservable<byte[]> ChangesAsBytes(LocalStorageProviderQueryOptions query)
    {
        return ProviderOptions.Store.Changes;
    }
}
