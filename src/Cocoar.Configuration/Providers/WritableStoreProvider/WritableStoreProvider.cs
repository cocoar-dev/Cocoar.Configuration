using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Provider that reads from a <see cref="WritableStoreState"/>.
/// The store is shared state owned by the closure in <c>FromStore()</c> —
/// the provider does NOT own or dispose it.
/// </summary>
public sealed class WritableStoreProvider(WritableStoreProviderOptions options)
    : ConfigurationProvider<WritableStoreProviderOptions, WritableStoreProviderQueryOptions>(options)
{
    /// <summary>
    /// The store backing this provider. Exposed so the overlay layer can be located in the rule
    /// pipeline by store reference (used for base/prefix computation and provenance).
    /// </summary>
    internal WritableStoreState Store => ProviderOptions.Store;

    public override async Task<byte[]> FetchConfigurationBytesAsync(
        WritableStoreProviderQueryOptions query, CancellationToken ct = default)
    {
        return await ProviderOptions.Store.ReadBytesAsync(ct).ConfigureAwait(false);
    }

    public override IObservable<byte[]> ChangesAsBytes(WritableStoreProviderQueryOptions query)
    {
        return ProviderOptions.Store.Changes;
    }
}
