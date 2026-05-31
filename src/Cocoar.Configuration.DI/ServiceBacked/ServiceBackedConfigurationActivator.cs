using Cocoar.Configuration.Core;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Activates service-backed (Layer-2, ADR-006) configuration on host start. Implemented as
/// <see cref="IHostedLifecycleService"/> and acting in <see cref="StartingAsync"/> — <em>before</em> any
/// regular <c>IHostedService.StartAsync</c> — so Layer-2 values are live before application code and other
/// hosted services read configuration. It publishes the root provider to the holder and awaits the activation
/// recompute, satisfying the readiness contract.
/// </summary>
internal sealed class ServiceBackedConfigurationActivator : IHostedLifecycleService
{
    private readonly ConfigManager _manager;
    private readonly ServiceProviderHolder _holder;
    private readonly int _startIndex;
    private readonly IServiceProvider _rootServiceProvider;

    public ServiceBackedConfigurationActivator(
        ConfigManager manager,
        ServiceProviderHolder holder,
        int startIndex,
        IServiceProvider rootServiceProvider)
    {
        _manager = manager;
        _holder = holder;
        _startIndex = startIndex;
        _rootServiceProvider = rootServiceProvider;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
        => ServiceBackedConfigurationCoordinator.ActivateAsync(_manager, _holder, _startIndex, _rootServiceProvider);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
