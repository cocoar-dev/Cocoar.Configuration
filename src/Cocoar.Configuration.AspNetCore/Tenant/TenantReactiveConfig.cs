using Cocoar.Configuration.Core;
using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Scoped adapter that binds <see cref="ITenantReactiveConfig{T}"/> to the current request's tenant
/// (from <see cref="ITenantContext"/>) and delegates to <c>ConfigManager.GetReactiveConfigForTenant&lt;T&gt;</c>.
/// </summary>
internal sealed class TenantReactiveConfig<T> : ITenantReactiveConfig<T>
{
    private readonly Lazy<IReactiveConfig<T>> _inner;

    public TenantReactiveConfig(ConfigManager configManager, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(tenantContext);

        // Resolved lazily on first use: the tenant pipeline must already be initialized (e.g. via
        // EnsureTenantInitializedAsync in middleware). Binding to ITenantContext.Current here ties this scoped
        // view to THIS request's tenant.
        _inner = new Lazy<IReactiveConfig<T>>(() =>
        {
            var tenant = tenantContext.Current;
            if (string.IsNullOrWhiteSpace(tenant))
            {
                throw new InvalidOperationException(
                    "No tenant resolved in ITenantContext for the current request. ITenantReactiveConfig<T> " +
                    "requires a tenant; a singleton must use ConfigManager.GetReactiveConfigForTenant<T>(id) explicitly.");
            }

            return configManager.GetReactiveConfigForTenant<T>(tenant);
        });
    }

    public T CurrentValue => _inner.Value.CurrentValue;

    public IDisposable Subscribe(IObserver<T> observer) => _inner.Value.Subscribe(observer);
}
