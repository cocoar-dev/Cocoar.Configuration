using System.Reactive.Linq;
using System.Text.Json;

using Cocoar.Configuration.Core.Tests.Helpers;

namespace Cocoar.Configuration.Core.Tests.TestUtilities;

/// <summary>
/// Test-only provider that can be configured to fail on demand.
/// Used to test ConfigManager error handling behavior for required vs optional rules.
/// Supports various failure scenarios: immediate, after N calls, or toggle-based.
/// </summary>
public sealed class FailableProvider : ConfigurationProvider<FailableProviderOptions, FailableProviderQuery>
{
    private int _callCount = 0;

    public FailableProvider(FailableProviderOptions options) : base(options)
    {
    }

    public override Task<JsonElement> FetchConfigurationAsync(FailableProviderQuery query, CancellationToken ct = default)
    {
        _callCount++;

        // Check if we should fail based on the configured scenario
        var shouldFail = ProviderOptions.FailureMode switch
        {
            FailureMode.Never => false,
            FailureMode.Always => true,
            FailureMode.AfterNCalls => _callCount > ProviderOptions.FailAfterCallCount,
            FailureMode.OnSpecificCall => _callCount == ProviderOptions.FailOnCallNumber,
            FailureMode.QueryControlled => query.ShouldFail,
            _ => false
        };

        if (shouldFail)
        {
            var message = ProviderOptions.FailureMode == FailureMode.QueryControlled 
                ? query.FailureMessage 
                : $"FailableProvider configured to fail (Mode: {ProviderOptions.FailureMode}, Call: {_callCount})";
            
            throw new InvalidOperationException(message);
        }

        // Return the configured JSON data
        return Task.FromResult(ProviderOptions.JsonData);
    }

    public override IObservable<JsonElement> Changes(FailableProviderQuery query) =>
        // For testing, we don't need change notifications
        Observable.Empty<JsonElement>();
}

/// <summary>
/// Defines different failure modes for testing various error scenarios.
/// </summary>
public enum FailureMode
{
    /// <summary>Never fail - provider always succeeds.</summary>
    Never,
    
    /// <summary>Always fail - provider fails on every call.</summary>
    Always,
    
    /// <summary>Fail after N successful calls - simulates runtime failures like file corruption.</summary>
    AfterNCalls,
    
    /// <summary>Fail only on a specific call number - for precise testing.</summary>
    OnSpecificCall,
    
    /// <summary>Failure controlled by query parameter - original behavior.</summary>
    QueryControlled
}

/// <summary>
/// Options for the FailableProvider - contains the JSON data to return when not failing.
/// </summary>
public sealed class FailableProviderOptions : IProviderConfiguration
{
    public JsonElement JsonData { get; }
    public FailureMode FailureMode { get; }
    public int FailAfterCallCount { get; }
    public int FailOnCallNumber { get; }

    public FailableProviderOptions(
        JsonElement jsonData, 
        FailureMode failureMode = FailureMode.Never,
        int failAfterCallCount = 0,
        int failOnCallNumber = 1)
    {
        JsonData = jsonData;
        FailureMode = failureMode;
        FailAfterCallCount = failAfterCallCount;
        FailOnCallNumber = failOnCallNumber;
    }

    public FailableProviderOptions(
        string json, 
        FailureMode failureMode = FailureMode.Never,
        int failAfterCallCount = 0,
        int failOnCallNumber = 1)
    {
        using var document = JsonDocument.Parse(json);
        JsonData = document.RootElement.Clone();
        FailureMode = failureMode;
        FailAfterCallCount = failAfterCallCount;
        FailOnCallNumber = failOnCallNumber;
    }

    // Convenience factory methods for common scenarios
    public static FailableProviderOptions AlwaysSucceed(string json) =>
        new(json, FailureMode.Never);

    public static FailableProviderOptions AlwaysFail(string json) =>
        new(json, FailureMode.Always);

    public static FailableProviderOptions FailAfterNCalls(string json, int callsBeforeFailure) =>
        new(json, FailureMode.AfterNCalls, failAfterCallCount: callsBeforeFailure);

    public static FailableProviderOptions FailOnCall(string json, int callNumber) =>
        new(json, FailureMode.OnSpecificCall, failOnCallNumber: callNumber);

    public static FailableProviderOptions QueryControlled(string json) =>
        new(json, FailureMode.QueryControlled);

    // Don't share instances - each test should have isolated providers
    public string? GenerateProviderKey() => null;
}

/// <summary>
/// Query options for the FailableProvider - controls whether the provider should fail.
/// </summary>
public sealed class FailableProviderQuery : IProviderQuery
{
    public bool ShouldFail { get; }
    public string FailureMessage { get; }

    public FailableProviderQuery(bool shouldFail = false, string failureMessage = "Provider failed")
    {
        ShouldFail = shouldFail;
        FailureMessage = failureMessage;
    }

    public static readonly FailableProviderQuery Success = new(false);
    public static readonly FailableProviderQuery Failure = new(true);
}
