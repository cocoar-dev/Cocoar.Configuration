using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class RuleHealthEntry
{
    public int Index { get; }
    public string? Name { get; }
    public bool Required { get; }
    public RuleResultStatus Status { get; }
    public DateTime? LastSuccessUtc { get; }
    public DateTime? LastFailureUtc { get; }
    public int FailureCount { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public RuleHealthEntry(int index, string? name, bool required, RuleResultStatus status,
        DateTime? lastSuccessUtc, DateTime? lastFailureUtc, int failureCount, string? errorCode, string? errorMessage)
    {
        Index = index;
        Name = name;
        Required = required;
        Status = status;
        LastSuccessUtc = lastSuccessUtc;
        LastFailureUtc = lastFailureUtc;
        FailureCount = failureCount;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public RuleHealthEntry WithStatus(RuleResultStatus status, DateTime utcNow, string? errorCode = null, string? errorMessage = null)
    {
        var lastSuccess = status == RuleResultStatus.Up ? utcNow : LastSuccessUtc;
        var lastFailure = status == RuleResultStatus.Down ? utcNow : LastFailureUtc;
        var failureCount = status == RuleResultStatus.Down ? FailureCount + 1 : (status == RuleResultStatus.Up ? 0 : FailureCount);
        return new RuleHealthEntry(Index, Name, Required, status, lastSuccess, lastFailure, failureCount, errorCode, errorMessage);
    }
}

public sealed class ConfigHealthSnapshot
{
    public long Id { get; }
    public DateTime TimestampUtc { get; }
    public long ConfigVersion { get; }
    public HealthStatus OverallStatus { get; }
    public IReadOnlyList<RuleHealthEntry> Rules { get; }
    public SummaryInfo Summary { get; }

    public sealed class SummaryInfo
    {
        public int Total { get; }
        public int RequiredFailed { get; }
        public int OptionalFailed { get; }
        public int Skipped { get; }
        public SummaryInfo(int total, int requiredFailed, int optionalFailed, int skipped)
        { Total = total; RequiredFailed = requiredFailed; OptionalFailed = optionalFailed; Skipped = skipped; }
    }

    public ConfigHealthSnapshot(long id, DateTime timestampUtc, long configVersion, IReadOnlyList<RuleHealthEntry> rules)
    {
        Id = id;
        TimestampUtc = timestampUtc;
        ConfigVersion = configVersion;
        Rules = rules;
        Summary = BuildSummary(rules);
        OverallStatus = DeriveStatus(rules);
    }

    private static SummaryInfo BuildSummary(IReadOnlyList<RuleHealthEntry> rules)
    {
        int total = rules.Count;
        int requiredFailed = rules.Count(r => r.Required && r.Status == RuleResultStatus.Down);
        int optionalFailed = rules.Count(r => !r.Required && r.Status == RuleResultStatus.Down);
        int skipped = rules.Count(r => r.Status == RuleResultStatus.Skipped);
        return new SummaryInfo(total, requiredFailed, optionalFailed, skipped);
    }

    private static HealthStatus DeriveStatus(IReadOnlyList<RuleHealthEntry> rules)
    {
        if (rules.Count == 0) return HealthStatus.Unknown;
        bool anyRequiredDown = false;
        bool anyOptionalDown = false;
        bool anyUnknown = false;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            switch (r.Status)
            {
                case RuleResultStatus.Down:
                    if (r.Required) anyRequiredDown = true; else anyOptionalDown = true;
                    break;
                case RuleResultStatus.Unknown:
                    anyUnknown = true;
                    break;
            }
            if (anyRequiredDown) break; // highest precedence
        }
        if (anyRequiredDown) return HealthStatus.Unhealthy;
        if (anyOptionalDown) return HealthStatus.Degraded;
        if (anyUnknown) return HealthStatus.Unknown;
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

internal sealed class ConfigurationHealthService : IConfigurationHealthService, IDisposable
{
    private readonly BehaviorSubject<ConfigHealthSnapshot> _subject;
    private readonly BehaviorSubject<HealthStatus> _statusSubject;
    public ConfigurationHealthService(ConfigHealthSnapshot initial)
    {
        _subject = new BehaviorSubject<ConfigHealthSnapshot>(initial);
        _statusSubject = new BehaviorSubject<HealthStatus>(initial.OverallStatus);
    }

    public HealthStatus Status => _statusSubject.Value;
    public ConfigHealthSnapshot Snapshot => _subject.Value;
    public IObservable<ConfigHealthSnapshot> SnapshotStream => _subject;
    public IObservable<HealthStatus> StatusStream => _statusSubject;
    public bool IsHealthy => Status == HealthStatus.Healthy;

    public void Publish(ConfigHealthSnapshot snapshot)
    {
        var current = _subject.Value;
        // Suppress identical snapshots (same overall + rule statuses + config version)
        if (current.OverallStatus == snapshot.OverallStatus && current.ConfigVersion == snapshot.ConfigVersion)
        {
            bool same = current.Rules.Count == snapshot.Rules.Count;
            if (same)
            {
                for (int i = 0; i < current.Rules.Count; i++)
                {
                    var a = current.Rules[i];
                    var b = snapshot.Rules[i];
                    if (a.Status != b.Status || a.FailureCount != b.FailureCount)
                    { same = false; break; }
                }
            }
            if (same) return;
        }
        _subject.OnNext(snapshot);
        if (_statusSubject.Value != snapshot.OverallStatus)
            _statusSubject.OnNext(snapshot.OverallStatus);
    }

    public void Dispose()
    {
        _subject.OnCompleted();
        _statusSubject.OnCompleted();
        _subject.Dispose();
        _statusSubject.Dispose();
    }
}

public sealed class DuplicateRuleNameException : Exception
{
    public DuplicateRuleNameException(string name)
        : base($"Duplicate rule name detected: '{name}'. Rule names must be unique (case-insensitive).") { }
}

internal static class HealthErrorCodes
{
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string FileIoError = "FILE_IO_ERROR";
    public const string JsonParse = "JSON_PARSE";
    public const string HttpTimeout = "HTTP_TIMEOUT";
    public const string HttpErrorStatus = "HTTP_ERROR_STATUS";
    public const string ProviderCanceled = "PROVIDER_CANCELED";
}