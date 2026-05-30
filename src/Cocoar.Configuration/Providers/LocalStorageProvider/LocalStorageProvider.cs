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
    /// <summary>
    /// The store backing this provider. Exposed so the overlay layer can be located in the rule
    /// pipeline by store reference (used for base/prefix computation and provenance).
    /// </summary>
    internal LocalStorageStore Store => ProviderOptions.Store;

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
