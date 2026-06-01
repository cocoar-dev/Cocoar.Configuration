using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration.Rules;

/// <summary>
/// Manages change subscriptions to configuration providers.
/// Handles subscription lifecycle, query key tracking, and change notifications.
/// </summary>
internal sealed class ChangeSubscription : IDisposable
{
    private readonly SimpleSubject<bool> _changes = new();
    private IDisposable? _subscription;
    private string? _queryKey;

    /// <summary>
    /// Observable stream of change notifications.
    /// </summary>
    public IObservable<bool> Changes => _changes;

    /// <summary>
    /// Gets the current query key used for the subscription.
    /// </summary>
    public string? QueryKey => _queryKey;

    /// <summary>
    /// Checks if a subscription is currently active.
    /// </summary>
    public bool IsSubscribed => _subscription is not null;

    /// <summary>
    /// Ensures subscription is active for the given query.
    /// Returns true if subscription was recreated (query key changed).
    /// </summary>
    public bool EnsureSubscription(
        ConfigurationProvider provider,
        IProviderQuery queryOptions,
        string queryKey,
        Action<byte[]> onChangeCallback)
    {
        if (_subscription is not null && _queryKey == queryKey)
        {
            return false;
        }
        Unsubscribe();
        _queryKey = queryKey;

        _subscription = provider
            .ChangesAsBytes(queryOptions)
            .Subscribe(
                bytes => onChangeCallback(bytes),
                _ =>
                {
                    // Provider errored — unsubscribe the dead subscription before
                    // notifying, so the next recompute re-subscribes cleanly.
                    Unsubscribe();
                    PublishChangeSafely();
                });

        return true; // Subscription was recreated
    }

    /// <summary>
    /// Publishes a change notification to subscribers.
    /// </summary>
    public void PublishChangeSafely()
    {
        Safety.NotifyQuietly(_changes, true);
    }

    /// <summary>
    /// Unsubscribes from the current provider changes.
    /// </summary>
    public void Unsubscribe()
    {
        if (_subscription is not null)
        {
            Safety.DisposeQuietly(_subscription);
            _subscription = null;
        }
    }

    /// <summary>
    /// Resets subscription state (used when provider changes).
    /// </summary>
    public void Reset()
    {
        Unsubscribe();
        _queryKey = null;
    }

    public void Dispose()
    {
        Unsubscribe();
        _changes.OnCompleted();
        _changes.Dispose();
    }
}
