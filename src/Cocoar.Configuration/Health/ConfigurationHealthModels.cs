using System.Reactive.Subjects;

namespace Cocoar.Configuration.Health;

public enum HealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3
}

public enum RuleResultStatus
{
    Unknown = 0,
    Up = 1,
    Down = 2,
    Skipped = 3
}

public sealed class RuleHealthEntry(
    int index,
    string? name,
    bool required,
    RuleResultStatus status,
    DateTime? lastSuccessUtc,
    DateTime? lastFailureUtc,
    int failureCount,
    string? errorCode,
    string? errorMessage)
{
    public int Index { get; } = index;
    public string? Name { get; } = name;
    public bool Required { get; } = required;
    public RuleResultStatus Status { get; } = status;
    public DateTime? LastSuccessUtc { get; } = lastSuccessUtc;
    public DateTime? LastFailureUtc { get; } = lastFailureUtc;
    public int FailureCount { get; } = failureCount;
    public string? ErrorCode { get; } = errorCode;
    public string? ErrorMessage { get; } = errorMessage;

    public RuleHealthEntry WithStatus(RuleResultStatus status, DateTime utcNow, string? errorCode = null, string? errorMessage = null)
    {
        var lastSuccess = status == RuleResultStatus.Up ? utcNow : LastSuccessUtc;
        var lastFailure = status == RuleResultStatus.Down ? utcNow : LastFailureUtc;
        var failureCount = status == RuleResultStatus.Down ? FailureCount + 1 : (status == RuleResultStatus.Up ? 0 : FailureCount);
        return new(Index, Name, Required, status, lastSuccess, lastFailure, failureCount, errorCode, errorMessage);
    }
}

public sealed class ConfigHealthSnapshot(
    long id,
    DateTime timestampUtc,
    long configVersion,
    IReadOnlyList<RuleHealthEntry> rules)
{
    public long Id { get; } = id;
    public DateTime TimestampUtc { get; } = timestampUtc;
    public long ConfigVersion { get; } = configVersion;
    public HealthStatus OverallStatus { get; } = DeriveStatus(rules);
    public IReadOnlyList<RuleHealthEntry> Rules { get; } = rules;
    public SummaryInfo Summary { get; } = BuildSummary(rules);

    public sealed class SummaryInfo(int total, int requiredFailed, int optionalFailed, int skipped)
    {
        public int Total { get; } = total;
        public int RequiredFailed { get; } = requiredFailed;
        public int OptionalFailed { get; } = optionalFailed;
        public int Skipped { get; } = skipped;
    }

    private static SummaryInfo BuildSummary(IReadOnlyList<RuleHealthEntry> rules)
    {
        var total = rules.Count;
        var requiredFailed = rules.Count(r => r is { Required: true, Status: RuleResultStatus.Down });
        var optionalFailed = rules.Count(r => r is { Required: false, Status: RuleResultStatus.Down });
        var skipped = rules.Count(r => r.Status == RuleResultStatus.Skipped);
        return new(total, requiredFailed, optionalFailed, skipped);
    }

    private static HealthStatus DeriveStatus(IReadOnlyList<RuleHealthEntry> rules)
    {
        if (rules.Count == 0)
        {
            return HealthStatus.Unknown;
        }

        var anyRequiredDown = false;
        var anyOptionalDown = false;
        var anyUnknown = false;
        foreach (var r in rules)
        {
            switch (r.Status)
            {
                case RuleResultStatus.Down:
                    if (r.Required)
                    {
                        anyRequiredDown = true;
                    }
                    else
                    {
                        anyOptionalDown = true;
                    }

                    break;
                case RuleResultStatus.Unknown:
                    anyUnknown = true;
                    break;
            }
            if (anyRequiredDown)
            {
                break;
            }
        }
        if (anyRequiredDown)
        {
            return HealthStatus.Unhealthy;
        }

        if (anyOptionalDown)
        {
            return HealthStatus.Degraded;
        }

        if (anyUnknown)
        {
            return HealthStatus.Unknown;
        }

        return HealthStatus.Healthy;
    }
}

public interface IConfigurationHealthService
{
    HealthStatus Status { get; }
    ConfigHealthSnapshot Snapshot { get; }
    IObservable<ConfigHealthSnapshot> SnapshotStream { get; }
    IObservable<HealthStatus> StatusStream { get; }
    bool IsHealthy { get; }
}

internal sealed class ConfigurationHealthService(ConfigHealthSnapshot initial)
    : IConfigurationHealthService, IDisposable
{
    private readonly BehaviorSubject<ConfigHealthSnapshot> _subject = new(initial);
    private readonly BehaviorSubject<HealthStatus> _statusSubject = new(initial.OverallStatus);

    public HealthStatus Status => _statusSubject.Value;
    public ConfigHealthSnapshot Snapshot => _subject.Value;
    public IObservable<ConfigHealthSnapshot> SnapshotStream => _subject;
    public IObservable<HealthStatus> StatusStream => _statusSubject;
    public bool IsHealthy => Status == HealthStatus.Healthy;

    public void Publish(ConfigHealthSnapshot snapshot)
    {
        var current = _subject.Value;
        
        if (current.OverallStatus == snapshot.OverallStatus && current.ConfigVersion == snapshot.ConfigVersion)
        {
            var same = current.Rules.Count == snapshot.Rules.Count;
            if (same)
            {
                for (var i = 0; i < current.Rules.Count; i++)
                {
                    var a = current.Rules[i];
                    var b = snapshot.Rules[i];
                    if (a.Status != b.Status || a.FailureCount != b.FailureCount)
                    { same = false; break; }
                }
            }
            if (same)
            {
                return;
            }
        }
        _subject.OnNext(snapshot);
        if (_statusSubject.Value != snapshot.OverallStatus)
        {
            _statusSubject.OnNext(snapshot.OverallStatus);
        }
    }

    public void Dispose()
    {
        _subject.OnCompleted();
        _statusSubject.OnCompleted();
        _subject.Dispose();
        _statusSubject.Dispose();
    }
}

