using Cocoar.Configuration.Core;
using Cocoar.Configuration.WritableStore;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class WritableStoreProviderOptions(WritableStoreState store)
    : IProviderConfiguration, IProviderServiceRegistration
{
    public WritableStoreState Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Returns null to indicate non-reusable. Each WritableStore rule gets its own provider instance
    /// because each is backed by a unique <see cref="WritableStoreState"/> tied to a specific configuration type.
    /// </summary>
    public string? GenerateProviderKey() => null;

    /// <summary>
    /// Registers the overlay adapter as a singleton built at resolve time (so it can pull
    /// <see cref="ConfigManager"/> for base/prefix computation), then resolves both
    /// <see cref="IWritableStore{T}"/> and <see cref="IWritableStoreOverlay{T}"/> to that one instance.
    /// </summary>
    public IEnumerable<ProviderServiceRegistration> GetServiceRegistrations(Type concreteType)
    {
        var adapterType = typeof(WritableStoreAdapter<>).MakeGenericType(concreteType);
        var storageInterface = typeof(IWritableStore<>).MakeGenericType(concreteType);
        var overlayInterface = typeof(IWritableStoreOverlay<>).MakeGenericType(concreteType);
        var store = Store;

        yield return ProviderServiceRegistration.Singleton(adapterType, sp =>
        {
            var configManager = (ConfigManager?)sp.GetService(typeof(ConfigManager))
                ?? throw new InvalidOperationException(
                    "ConfigManager is not registered. Call AddCocoarConfiguration before resolving IWritableStore<T>.");
            return Activator.CreateInstance(adapterType, configManager, store)!;
        });

        yield return ProviderServiceRegistration.Singleton(storageInterface,
            sp => sp.GetService(adapterType)!);

        yield return ProviderServiceRegistration.Singleton(overlayInterface,
            sp => sp.GetService(adapterType)!);
    }
}
