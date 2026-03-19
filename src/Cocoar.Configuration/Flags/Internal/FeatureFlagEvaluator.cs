using Cocoar.Configuration.Diagnostics;
using System.Diagnostics;

namespace Cocoar.Configuration.Flags.Internal;

/// <summary>
/// Default implementation of <see cref="IFeatureFlagEvaluator"/>. Registered as Scoped in DI so the
/// service provider it holds is the current request's scope — resolvers are resolved from that
/// scope, not the root container, which allows resolvers to have Scoped dependencies (e.g. DbContext).
/// FeatureFlag classes are Singleton and resolve cleanly from any scope.
/// </summary>
internal sealed class FeatureFlagEvaluator : IFeatureFlagEvaluator
{
    private readonly IReadOnlyDictionary<string, FlagEvaluationEntry> _entries;
    private readonly IServiceProvider _services;

    internal FeatureFlagEvaluator(
        IReadOnlyDictionary<string, FlagEvaluationEntry> entries,
        IServiceProvider services)
    {
        _entries = entries;
        _services = services;
    }

    public bool CanEvaluate(string key) => _entries.ContainsKey(key);

    public async Task<object?> EvaluateAsync(
        string key,
        object resolverRequest,
        CancellationToken cancellationToken = default)
    {
        using var activity = CocoarMetrics.ActivitySource.StartActivity("cocoar.feature_flag.evaluate");
        activity?.SetTag("flag.key", key);
        activity?.SetTag("flag.kind", "feature_flag");

        if (!_entries.TryGetValue(key, out var entry))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Key not found");
            var available = string.Join(", ", _entries.Keys);
            throw new KeyNotFoundException(
                $"No contextual feature flag evaluation entry found for key '{key}'. " +
                $"Registered keys: [{available}]");
        }

        var resolver = _services.GetService(entry.Resolver.ResolverType)
            ?? throw new InvalidOperationException(
                $"No service of type '{entry.Resolver.ResolverType.Name}' is registered. " +
                $"Ensure the resolver is registered via resolvers.Global<T>() or resolvers.For<T>(...).");

        var context = await entry.CompiledResolveAsync(resolver, resolverRequest).ConfigureAwait(false);

        var flagClass = _services.GetService(entry.FlagClassType)
            ?? throw new InvalidOperationException(
                $"No service of type '{entry.FlagClassType.Name}' is registered. " +
                $"Ensure the flag class is registered via Register<{entry.FlagClassType.Name}>().");

        try
        {
            var result = entry.CompiledFlagInvoke(flagClass, context);

            activity?.SetTag("flag.status", "success");
            CocoarMetrics.FlagEvaluations.Add(1,
                new KeyValuePair<string, object?>("key", key),
                new KeyValuePair<string, object?>("kind", "feature_flag"),
                new KeyValuePair<string, object?>("status", "success"));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("flag.status", "failure");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            CocoarMetrics.FlagEvaluations.Add(1,
                new KeyValuePair<string, object?>("key", key),
                new KeyValuePair<string, object?>("kind", "feature_flag"),
                new KeyValuePair<string, object?>("status", "failure"));

            throw new InvalidOperationException(
                $"Feature flag evaluation '{entry.FlagClassType.Name}.{entry.Property.Name}' threw an exception.",
                ex);
        }
    }
}
