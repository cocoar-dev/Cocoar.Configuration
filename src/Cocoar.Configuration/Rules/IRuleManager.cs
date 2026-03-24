using Cocoar.Configuration.Core;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Rules;

/// <summary>
/// Outcome of the last rule execution, used by health tracking and diagnostics.
/// </summary>
internal enum RuleExecutionOutcome
{
    Unknown = 0,
    Up = 1,
    Skipped = 2,
    Failed = 3
}

/// <summary>
/// Common interface for rule managers. Implemented by both <see cref="RuleManager"/>
/// (single provider rule) and <see cref="AggregateRuleManager"/> (group of sub-rules).
/// The engine and health tracker work exclusively through this interface.
/// </summary>
internal interface IRuleManager : IDisposable
{
    Type TypeDefinition { get; }
    bool Required { get; }
    IObservable<bool> Changes { get; }

    RuleExecutionOutcome LastOutcome { get; }
    Exception? LastFailureException { get; }
    MutableJsonObject? LastJsonContribution { get; set; }
    string? LastSelectionHash { get; set; }

    Task<ReadOnlyMemory<byte>?> ComputeAsync(IConfigurationAccessor accessor, CancellationToken ct);

    void ClearCachedBytes();
    void UpdateCachedBytes(byte[] encryptedBytes);

    /// <summary>
    /// Sub-rule managers for aggregate rules. Null for single-provider rules.
    /// Used by ConfigHub for drill-down into aggregate structure.
    /// </summary>
    IReadOnlyList<IRuleManager>? SubManagers { get; }
}
