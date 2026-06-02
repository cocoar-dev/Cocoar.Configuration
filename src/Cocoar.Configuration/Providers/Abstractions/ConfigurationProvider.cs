using System.Text.Json;
using Cocoar.Configuration.Helper;

namespace Cocoar.Configuration.Providers.Abstractions;

/// <summary>
/// Base class for configuration providers. The currency is always raw UTF-8 JSON <c>byte[]</c> (never strings)
/// so sensitive payloads can be zeroed by consumers.
/// <para><b>Provider contract — invariants implementations must honor:</b></para>
/// <list type="bullet">
/// <item><b>Empty, never null.</b> Both methods deal in a JSON value; "no data" is an empty object
/// (<c>{}</c>), never a <see langword="null"/> <c>byte[]</c> (ADR-003). The merge layer treats <c>{}</c> as an
/// invisible layer.</item>
/// <item><b>Don't retain payload bytes.</b> Return fresh arrays a caller may zero; never cache secret bytes on
/// the provider.</item>
/// <item><b>Disposal is opt-in but expected.</b> If the provider owns resources (file watchers, timers,
/// subscriptions, a <see cref="System.Threading.CancellationTokenSource"/>), implement <see cref="System.IDisposable"/> —
/// the provider registry disposes providers that implement it.</item>
/// <item><b>Shared instances must be thread-safe.</b> Providers with an equal
/// <see cref="IProviderConfiguration.GenerateProviderKey"/> are shared across rules, so a shared instance may
/// receive concurrent <see cref="FetchConfigurationBytesAsync"/> / <see cref="ChangesAsBytes"/> calls.</item>
/// </list>
/// </summary>
public abstract class ConfigurationProvider
{
    /// <summary>
    /// Fetches the current configuration snapshot as raw UTF-8 JSON bytes (no string allocations, so secrets can
    /// be zeroed by the consumer).
    /// <para>
    /// Return an empty object (<c>"{}"u8</c>) when no value is available — never <see langword="null"/>. This
    /// method <b>may throw</b> to signal a hard failure: the recompute then rolls back for a <c>Required</c> rule
    /// (health → Unhealthy, startup exception) or degrades for an optional one — the throw is how the two are
    /// distinguished, so don't swallow a genuine failure into <c>{}</c> here. Honor <paramref name="ct"/>.
    /// </para>
    /// </summary>
    public abstract Task<byte[]> FetchConfigurationBytesAsync(IProviderQuery query, CancellationToken ct = default);

    /// <summary>
    /// Observes configuration changes as raw UTF-8 JSON bytes. Emit the fresh snapshot on each change.
    /// <para>
    /// The engine handles a faulting stream gracefully: an <see cref="IObserver{T}.OnError"/> causes it to
    /// unsubscribe and schedule a recompute (which re-subscribes and re-fetches). So on a transient failure a
    /// provider may either emit <c>{}</c> (degrade in-stream, like the file provider) or call <c>OnError</c> —
    /// both recover; just never let the stream hang. The subscription returned to a subscriber is the teardown
    /// handle (there is no <c>CancellationToken</c> on this method) — dispose it to stop observing.
    /// </para>
    /// </summary>
    public abstract IObservable<byte[]> ChangesAsBytes(IProviderQuery query);
}
