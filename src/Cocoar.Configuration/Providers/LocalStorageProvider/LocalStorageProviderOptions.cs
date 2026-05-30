using Cocoar.Configuration.Core;
using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class LocalStorageProviderOptions(LocalStorageStore store)
    : IProviderConfiguration, IProviderServiceRegistration
{
    public LocalStorageStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Returns null to indicate non-reusable. Each LocalStorage rule gets its own provider instance
    /// because each is backed by a unique <see cref="LocalStorageStore"/> tied to a specific configuration type.
    /// </summary>
    public string? GenerateProviderKey() => null;

    /// <summary>
    /// Registers the overlay adapter as a singleton built at resolve time (so it can pull
    /// <see cref="ConfigManager"/> for base/prefix computation), then resolves both
    /// <see cref="ILocalStorage{T}"/> and <see cref="ILocalStorageOverlay{T}"/> to that one instance.
    /// </summary>
    public IEnumerable<ProviderServiceRegistration> GetServiceRegistrations(Type concreteType)
    {
        var adapterType = typeof(LocalStorageAdapter<>).MakeGenericType(concreteType);
        var storageInterface = typeof(ILocalStorage<>).MakeGenericType(concreteType);
        var overlayInterface = typeof(ILocalStorageOverlay<>).MakeGenericType(concreteType);
        var store = Store;

        yield return ProviderServiceRegistration.Singleton(adapterType, sp =>
        {
            var configManager = (ConfigManager?)sp.GetService(typeof(ConfigManager))
                ?? throw new InvalidOperationException(
                    "ConfigManager is not registered. Call AddCocoarConfiguration before resolving ILocalStorage<T>.");
            return Activator.CreateInstance(adapterType, configManager, store)!;
        });

        yield return ProviderServiceRegistration.Singleton(storageInterface,
            sp => sp.GetService(adapterType)!);

        yield return ProviderServiceRegistration.Singleton(overlayInterface,
            sp => sp.GetService(adapterType)!);
    }
}
