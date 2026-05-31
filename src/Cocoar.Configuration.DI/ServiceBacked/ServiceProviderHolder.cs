namespace Cocoar.Configuration.DI;

/// <summary>
/// Holds the application root <see cref="IServiceProvider"/> for service-backed (Layer-2, ADR-006)
/// configuration. Null until the container is built; set once — on host start — by the activation hosted
/// service (or a manual <c>ActivateServiceBackedConfigurationAsync</c> call). The instance is captured in the
/// closures of the sp-gated Layer-2 rules so their factories can resolve services lazily at recompute time,
/// and is registered as a DI singleton so the activator receives the very same instance.
/// </summary>
internal sealed class ServiceProviderHolder
{
    private readonly object _gate = new();
    private volatile IServiceProvider? _serviceProvider;
    private Task? _activationTask;

    /// <summary>The application root provider once published; otherwise <c>null</c>.</summary>
    internal IServiceProvider? ServiceProvider => _serviceProvider;

    /// <summary>True once the container has been published (the Layer-2 activation recompute may run).</summary>
    internal bool HasServiceProvider => _serviceProvider is not null;

    /// <summary>
    /// Publishes the root provider and starts the activation recompute exactly once. Every caller — the CAS winner
    /// and any concurrent loser — receives the SAME activation <see cref="Task"/>, so all of them observe the
    /// readiness guarantee (not just the first), and the recompute runs once. The provider is published BEFORE the
    /// activation runs, so the sp-gated rules see it.
    /// </summary>
    internal Task Activate(IServiceProvider serviceProvider, Func<Task> activation)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(activation);

        lock (_gate)
        {
            if (_activationTask is not null)
            {
                return _activationTask;
            }

            _serviceProvider = serviceProvider;     // publish before the activation recompute reads it
            _activationTask = activation();
            return _activationTask;
        }
    }
}
