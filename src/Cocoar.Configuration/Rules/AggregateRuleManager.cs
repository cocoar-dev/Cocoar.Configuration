using Cocoar.Configuration.Core;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Reactive.Internal;
using Cocoar.Json.Mutable;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Rules;

internal static partial class AggregateRuleManagerLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Error, Message = "Required aggregate '{Name}' failed: {Reason}")]
    public static partial void RequiredAggregateFailed(this ILogger logger, Exception? exception, string Name, string Reason);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Optional aggregate '{Name}' degraded: {Reason}")]
    public static partial void OptionalAggregateDegraded(this ILogger logger, string Name, string Reason);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "Sub-rule {Index} in aggregate '{Name}' failed")]
    public static partial void SubRuleFailed(this ILogger logger, Exception exception, int Index, string Name);
}

/// <summary>
/// Manages a group of sub-rules as a single logical unit.
/// Executes sub-rules internally, merges their results, and presents one merged output to the engine.
/// Inner Required failures are contained within the aggregate boundary.
/// </summary>
internal sealed class AggregateRuleManager : IRuleManager
{
    private readonly AggregateConfigRule _rule;
    private readonly ILogger _logger;
    private readonly IRuleManager[] _internalManagers;
    private readonly SimpleSubject<bool> _changes = new();
    private readonly List<IDisposable> _changeSubscriptions = [];
    private readonly string _name;

    public AggregateRuleManager(AggregateConfigRule rule, ILogger logger, ProviderRegistry registry)
    {
        _rule = rule;
        _logger = logger;
        _name = rule.Options?.Name ?? "aggregate";

        _internalManagers = new IRuleManager[rule.SubRules.Count];
        for (var i = 0; i < rule.SubRules.Count; i++)
        {
            _internalManagers[i] = new RuleManager(rule.SubRules[i], logger, registry);
        }

        // Subscribe to all internal changes and forward to our own Changes observable
        for (var i = 0; i < _internalManagers.Length; i++)
        {
            var sub = _internalManagers[i].Changes.Subscribe(_ =>
            {
                _changes.OnNext(true);
            });
            _changeSubscriptions.Add(sub);
        }
    }

    public Type TypeDefinition => _rule.ConcreteType;
    public bool Required => _rule.Options?.Required == true;
    public IObservable<bool> Changes => _changes;
    public RuleExecutionOutcome LastOutcome { get; private set; } = RuleExecutionOutcome.Unknown;
    public Exception? LastFailureException { get; private set; }
    public MutableJsonObject? LastJsonContribution { get; set; }

    public string? LastSelectionHash { get; set; }

    public IReadOnlyList<IRuleManager>? SubManagers => _internalManagers;

    public async Task<ReadOnlyMemory<byte>?> ComputeAsync(IConfigurationAccessor accessor, CancellationToken ct)
    {
        LastFailureException = null;

        // Aggregate-level When predicate
        if (_rule.Options?.UseWhen is { } predicate && !predicate(accessor))
        {
            LastOutcome = RuleExecutionOutcome.Skipped;
            return null;
        }

        MutableJsonObject? merged = null;
        var anyContributed = false;
        var criticalFailed = false;
        Exception? firstFailure = null;

        for (var i = 0; i < _internalManagers.Length; i++)
        {
            var rm = _internalManagers[i];
            try
            {
                var bytes = await rm.ComputeAsync(accessor, ct).ConfigureAwait(false);
                if (bytes is { Length: > 0 } b)
                {
                    var node = MutableJsonDocument.Parse(b.Span);
                    if (node is MutableJsonObject obj && obj.Properties.Count > 0)
                    {
                        merged ??= new MutableJsonObject();
                        MutableJsonMerge.Merge(merged, obj);
                        anyContributed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Inner Required sub-rule threw — caught here, stays in aggregate boundary
                criticalFailed = true;
                firstFailure ??= ex;
                _logger.SubRuleFailed(ex, i, _name);
            }
        }

        if (criticalFailed || !anyContributed)
        {
            LastOutcome = RuleExecutionOutcome.Failed;
            LastFailureException = firstFailure ?? new InvalidOperationException(
                $"Aggregate '{_name}': no sub-rule contributed data.");

            if (Required)
            {
                var reason = criticalFailed
                    ? "a critical sub-rule failed"
                    : "no sub-rule contributed data";
                _logger.RequiredAggregateFailed(firstFailure, _name, reason);
                throw new InvalidOperationException(
                    $"Required aggregate '{_name}' failed: {reason}.", firstFailure);
            }

            _logger.OptionalAggregateDegraded(_name,
                criticalFailed ? "a critical sub-rule failed" : "no sub-rule contributed data");
            LastJsonContribution = null;
            return EmptyObjectResult();
        }

        LastOutcome = RuleExecutionOutcome.Up;
        LastJsonContribution = merged;
        return MutableJsonDocument.ToUtf8Bytes(merged!);
    }

    public void ClearCachedBytes()
    {
        foreach (var rm in _internalManagers)
            rm.ClearCachedBytes();
    }

    public void UpdateCachedBytes(byte[] encryptedBytes)
    {
        // Aggregate doesn't cache a single encrypted blob — sub-rules handle their own caching.
    }

    public void Dispose()
    {
        foreach (var sub in _changeSubscriptions)
            sub.Dispose();
        _changeSubscriptions.Clear();

        foreach (var rm in _internalManagers)
            rm.Dispose();

        _changes.Dispose();
    }

    private static ReadOnlyMemory<byte> EmptyObjectResult()
        => "{}"u8.ToArray();
}
