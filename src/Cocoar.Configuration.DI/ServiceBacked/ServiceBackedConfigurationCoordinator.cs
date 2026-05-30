using Cocoar.Configuration.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Internal orchestration for service-backed (Layer-2, ADR-006) configuration: owns the holder lifecycle,
/// the DI wiring, and the two-phase activation recompute. Everything DI-specific lives here so the No-DI core
/// stays untouched.
/// </summary>
internal static class ServiceBackedConfigurationCoordinator
{
    /// <summary>
    /// Returns the holder attached to <paramref name="manager"/>, creating and attaching it on first use.
    /// Stored on the manager as <see cref="object"/> so the core never names a DI type; multiple
    /// <c>UseServiceBackedConfiguration</c> calls share one holder.
    /// </summary>
    internal static ServiceProviderHolder GetOrCreateHolder(ConfigManager manager)
    {
        if (manager.ServiceBackedHolder is ServiceProviderHolder existing)
        {
            return existing;
        }

        var holder = new ServiceProviderHolder();
        manager.ServiceBackedHolder = holder;
        return holder;
    }

    /// <summary>
    /// Registers the holder singleton and the activation hosted service — but ONLY when service-backed
    /// (Layer-2) rules were actually configured. Apps that do not opt in get zero impact (non-breaking rule 3).
    /// </summary>
    internal static void WireActivation(IServiceCollection services, ConfigManager manager)
    {
        if (manager.ServiceBackedHolder is not ServiceProviderHolder holder)
        {
            return;
        }

        var startIndex = manager.ServiceBackedLayerStartIndex;

        services.AddSingleton(holder);

        // Captures the ROOT provider (a singleton's factory sp is always root) so the MANUAL activation overload
        // can recover root even when handed a scoped provider — never capturing a scope that later disposes
        // (ADR-006 §9 lifetime discipline).
        services.AddSingleton(sp => new RootServiceProviderAccessor(sp));

        // AddSingleton<IHostedService> (not AddHostedService<T>) so we can capture holder/manager/index. The
        // factory's sp is the ROOT provider (singleton resolution).
        services.AddSingleton<IHostedService>(sp =>
            new ServiceBackedConfigurationActivator(manager, holder, startIndex, sp));
    }

    /// <summary>
    /// Activates Layer 2: publishes the root provider, then triggers a RECOMPUTE on the existing pipeline
    /// (never a rebuild) from the Layer-2 boundary. The prefix (Layer 1) is restored unchanged and the now
    /// un-gated Layer-2 suffix runs, so every live <c>IReactiveConfig&lt;T&gt;</c> view — whenever obtained —
    /// receives the update over the same backplane snapshot stream. Runs once; concurrent activators await the
    /// same activation (so all observe the readiness guarantee, not just the first).
    /// </summary>
    internal static Task ActivateAsync(
        ConfigManager manager,
        ServiceProviderHolder holder,
        int startIndex,
        IServiceProvider rootServiceProvider)
        => holder.Activate(rootServiceProvider, () => RunActivationAsync(manager, startIndex));

    private static async Task RunActivationAsync(ConfigManager manager, int startIndex)
    {
        var recomputeIndex = startIndex < 0 ? 0 : startIndex;

        // DIRECT, semaphore-guarded recompute awaited to completion — NOT the cancel-on-reschedule scheduler — so
        // a concurrent Layer-1 change cannot cancel activation before Layer 2 has committed (readiness, ADR-006 §7).
        try
        {
            await manager.RecomputeNowAsync(recomputeIndex).ConfigureAwait(false);
        }
        catch
        {
            // Optional Layer-2 sources degrade to the Layer-1 snapshot (engine logs + health). Never fault startup.
        }

        // Fan out to tenant pipelines built BEFORE activation — their sp-gated rules were skipped at init and
        // would otherwise never re-run. (Tenants built after activation already saw the active gate at init.)
        try
        {
            await manager.RecomputeInitializedTenantsNowAsync(recomputeIndex).ConfigureAwait(false);
        }
        catch
        {
            // Per-tenant failures are already isolated inside the fan-out; this is a final safety net.
        }
    }
}

/// <summary>
/// Captures the application <b>root</b> <see cref="IServiceProvider"/>. Registered as a singleton, so its factory
/// always receives the root provider — lets the manual activation overload recover root from any (even scoped)
/// provider it is handed.
/// </summary>
internal sealed class RootServiceProviderAccessor(IServiceProvider root)
{
    public IServiceProvider Root { get; } = root;
}
