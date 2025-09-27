using Cocoar.Configuration.Health;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core;

internal class ConfigurationHealthTracker
{
    private readonly List<RuleManager> _ruleManagers;
    private readonly ConfigurationHealthService _healthService;
    private long _healthSequence;
    private long _configVersion;

    public ConfigurationHealthTracker(List<RuleManager> ruleManagers, List<ConfigRule> rules)
    {
        _ruleManagers = ruleManagers;

        var initialEntries = rules.Select((r, i) => new RuleHealthEntry(
            index: i,
            name: r.Options?.MountPath,
            required: r.Options?.Required == true,
            status: RuleResultStatus.Unknown,
            lastSuccessUtc: null,
            lastFailureUtc: null,
            failureCount: 0,
            errorCode: null,
            errorMessage: null)).ToList();

        var initialSnapshot = new ConfigHealthSnapshot(
            id: ++_healthSequence,
            timestampUtc: DateTime.UtcNow,
            configVersion: _configVersion,
            rules: initialEntries);

        _healthService = new(initialSnapshot);
    }

    public IConfigurationHealthService GetHealthService() => _healthService;

    public void ReportSuccessfulRecompute(int startIndex)
    {
        var list = BuildEntriesFromOutcomes();
        PublishSnapshot(list, incrementVersion: true);
    }

    public void ReportFailedRecompute(int startIndex, Exception exception)
    {
        var list = BuildEntriesFromOutcomes(forceTrailingUnknown: true);
        PublishSnapshot(list, incrementVersion: false);
    }

    private void PublishSnapshot(List<RuleHealthEntry> entries, bool incrementVersion)
    {
        if (incrementVersion)
        {
            _configVersion++;
        }

        var snapshot = new ConfigHealthSnapshot(++_healthSequence, DateTime.UtcNow, _configVersion, entries);
        _healthService.Publish(snapshot);
    }

    private List<RuleHealthEntry> BuildEntriesFromOutcomes(bool forceTrailingUnknown = false)
    {
        var now = DateTime.UtcNow;
        var current = _healthService.Snapshot.Rules.ToDictionary(r => r.Index, r => r);
        var list = new List<RuleHealthEntry>(_ruleManagers.Count);

        for (var seed = 0; seed < _ruleManagers.Count; seed++)
        {
            if (current.TryGetValue(seed, out var existing))
            {
                list.Add(existing);
            }
            else
            {
                list.Add(new(seed, null, _ruleManagers[seed].Required, RuleResultStatus.Unknown, null, null, 0, null, null));
            }
        }

        var encounteredRequiredFailure = false;
        for (var i = 0; i < _ruleManagers.Count; i++)
        {
            var rm = _ruleManagers[i];
            var prev = list[i];
            RuleHealthEntry updated = prev;

            switch (rm.LastOutcome)
            {
                case RuleManager.RuleExecutionOutcome.Unknown:
                    updated = prev;
                    break;
                case RuleManager.RuleExecutionOutcome.Up:
                    updated = prev.Status != RuleResultStatus.Up ? prev.WithStatus(RuleResultStatus.Up, now) : prev;

                    break;
                case RuleManager.RuleExecutionOutcome.Skipped:
                    if (prev.Status != RuleResultStatus.Skipped)
                    {
                        updated = prev.WithStatus(RuleResultStatus.Skipped, now);
                    }

                    break;
                case RuleManager.RuleExecutionOutcome.Failed:
                    var ex = rm.LastFailureException ?? new Exception("FAILED");
                    updated = prev.WithStatus(RuleResultStatus.Down, now, MapException(ex), ShortMessage(ex));
                    if (rm.Required)
                    {
                        encounteredRequiredFailure = true;
                    }

                    break;
            }

            list[i] = updated;
            if (forceTrailingUnknown && encounteredRequiredFailure && i < _ruleManagers.Count - 1)
            {
                for (var j = i + 1; j < _ruleManagers.Count; j++)
                {
                    var existing = list[j];
                    if (existing.Status is RuleResultStatus.Up or RuleResultStatus.Skipped)
                    {
                        list[j] = new(existing.Index, existing.Name, existing.Required, RuleResultStatus.Unknown, existing.LastSuccessUtc, existing.LastFailureUtc, existing.FailureCount, existing.ErrorCode, existing.ErrorMessage);
                    }
                }
                break;
            }
        }
        return list;
    }

    private static string? MapException(Exception ex)
    {
        static bool TryGetCodeFromData(Exception e, out string? code)
        {
            code = null;
            if (e.Data is { Count: > 0 })
            {
                if (e.Data.Contains("HealthErrorCode") && e.Data["HealthErrorCode"] is string c1 && !string.IsNullOrWhiteSpace(c1))
                {
                    code = c1; return true;
                }
                if (e.Data.Contains("ErrorCode") && e.Data["ErrorCode"] is string c2 && !string.IsNullOrWhiteSpace(c2))
                {
                    code = c2; return true;
                }
            }
            return false;
        }

        if (TryGetCodeFromData(ex, out var codeFromEx))
        {
            return codeFromEx;
        }

        if (ex is AggregateException { InnerException: { } inner } && TryGetCodeFromData(inner, out var codeFromInner))
        {
            return codeFromInner;
        }

        return null;
    }

    private static string ShortMessage(Exception ex) => ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;

    public void Dispose()
    {
        _healthService.Dispose();
    }
}
