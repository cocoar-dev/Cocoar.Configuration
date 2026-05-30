using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// A <b>scoped</b> reactive configuration view bound to the current request's tenant (from
/// <see cref="ITenantContext"/>) — ADR-006 §11. Inject this into scoped/transient consumers to get THIS
/// tenant's effective configuration; it delegates to <c>ConfigManager.GetReactiveConfigForTenant&lt;T&gt;(tenant)</c>.
/// <para>
/// A singleton can never have an ambient tenant — it must call <c>GetReactiveConfigForTenant&lt;T&gt;(id)</c>
/// explicitly. This is a <b>distinct</b> interface from <see cref="IReactiveConfig{T}"/> (which stays the global,
/// singleton view), so injecting one never breaks the other (the ADR-006 §11 trap).
/// </para>
/// </summary>
public interface ITenantReactiveConfig<out T> : IReactiveConfig<T>
{
}
