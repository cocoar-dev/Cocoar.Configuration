using System.Reactive.Linq;
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

/// <summary>
/// Tracks the deserialization status for a configuration type.
/// </summary>
public sealed class DeserializationStatus
{
    /// <summary>
    /// Indicates whether deserialization was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message if deserialization failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// UTC timestamp of the last successful deserialization.
    /// </summary>
    public DateTime? LastSuccessUtc { get; }

    /// <summary>
    /// UTC timestamp of the last failed deserialization.
    /// </summary>
    public DateTime? LastFailureUtc { get; }

    /// <summary>
    /// Number of consecutive deserialization failures.
    /// </summary>
    public int FailureCount { get; }

    private DeserializationStatus(bool success, string? errorMessage, DateTime? lastSuccessUtc, DateTime? lastFailureUtc, int failureCount)
    {
        Success = success;
        ErrorMessage = errorMessage;
        LastSuccessUtc = lastSuccessUtc;
        LastFailureUtc = lastFailureUtc;
        FailureCount = failureCount;
    }

    /// <summary>
    /// Creates a successful deserialization status.
    /// </summary>
    public static DeserializationStatus CreateSuccess(DateTime utcNow, DeserializationStatus? previous = null)
    {
        return new DeserializationStatus(
            success: true,
            errorMessage: null,
            lastSuccessUtc: utcNow,
            lastFailureUtc: previous?.LastFailureUtc,
            failureCount: 0);
    }

    /// <summary>
    /// Creates a failed deserialization status.
    /// </summary>
    public static DeserializationStatus CreateFailure(string errorMessage, DateTime utcNow, DeserializationStatus? previous = null)
    {
        return new DeserializationStatus(
            success: false,
            errorMessage: errorMessage,
            lastSuccessUtc: previous?.LastSuccessUtc,
            lastFailureUtc: utcNow,
            failureCount: (previous?.FailureCount ?? 0) + 1);
    }
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
    string? errorMessage,
    string? providerType = null,
    string? configType = null,
    DeserializationStatus? deserializationStatus = null)
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
    public string? ProviderType { get; } = providerType;
    public string? ConfigType { get; } = configType;

    /// <summary>
    /// Tracks the deserialization status for this configuration type.
    /// Null if never attempted or if this is an older health entry.
    /// </summary>
    public DeserializationStatus? DeserializationStatus { get; } = deserializationStatus;

    public RuleHealthEntry WithStatus(RuleResultStatus status, DateTime utcNow, string? errorCode = null, string? errorMessage = null)
    {
        var lastSuccess = status == RuleResultStatus.Up ? utcNow : LastSuccessUtc;
        var lastFailure = status == RuleResultStatus.Down ? utcNow : LastFailureUtc;
        var failureCount = status == RuleResultStatus.Down ? FailureCount + 1 : FailureCount;

        // Update deserialization status on success
        var deserializationStatus = status == RuleResultStatus.Up
            ? Health.DeserializationStatus.CreateSuccess(utcNow, DeserializationStatus)
            : DeserializationStatus;

        return new(Index, Name, Required, status, lastSuccess, lastFailure, failureCount, errorCode, errorMessage, ProviderType, ConfigType, deserializationStatus);
    }

    /// <summary>
    /// Creates a new entry with a deserialization failure recorded.
    /// </summary>
    public RuleHealthEntry WithDeserializationFailure(string errorMessage)
    {
        var newStatus = Health.DeserializationStatus.CreateFailure(errorMessage, DateTime.UtcNow, DeserializationStatus);
        return new(Index, Name, Required, Status, LastSuccessUtc, LastFailureUtc, FailureCount, ErrorCode, ErrorMessage, ProviderType, ConfigType, newStatus);
    }

    /// <summary>
    /// Creates a new entry with a successful deserialization recorded.
    /// </summary>
    public RuleHealthEntry WithDeserializationSuccess()
    {
        var newStatus = Health.DeserializationStatus.CreateSuccess(DateTime.UtcNow, DeserializationStatus);
        return new(Index, Name, Required, Status, LastSuccessUtc, LastFailureUtc, FailureCount, ErrorCode, ErrorMessage, ProviderType, ConfigType, newStatus);
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

    public sealed class SummaryInfo(int total, int requiredFailed, int optionalFailed, int skipped, int deserializationFailures)
    {
        public int Total { get; } = total;
        public int RequiredFailed { get; } = requiredFailed;
        public int OptionalFailed { get; } = optionalFailed;
        public int Skipped { get; } = skipped;

        /// <summary>
        /// Number of rules with deserialization failures.
        /// </summary>
        public int DeserializationFailures { get; } = deserializationFailures;
    }

    private static SummaryInfo BuildSummary(IReadOnlyList<RuleHealthEntry> rules)
    {
        var total = rules.Count;
        var requiredFailed = rules.Count(r => r is { Required: true, Status: RuleResultStatus.Down });
        var optionalFailed = rules.Count(r => r is { Required: false, Status: RuleResultStatus.Down });
        var skipped = rules.Count(r => r.Status == RuleResultStatus.Skipped);
        var deserializationFailures = rules.Count(r => r.DeserializationStatus is { Success: false });
        return new(total, requiredFailed, optionalFailed, skipped, deserializationFailures);
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
        var anyDeserializationFailure = false;

        foreach (var r in rules)
        {
            // Check deserialization failures
            if (r.DeserializationStatus is { Success: false })
            {
                anyDeserializationFailure = true;
            }

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

        // Deserialization failures cause Degraded status
        if (anyDeserializationFailure || anyOptionalDown)
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
                    if (a.Status != b.Status || a.FailureCount != b.FailureCount ||
                        a.DeserializationStatus?.Success != b.DeserializationStatus?.Success)
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
