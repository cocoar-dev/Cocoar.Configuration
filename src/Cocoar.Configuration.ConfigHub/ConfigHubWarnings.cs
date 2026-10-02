namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// How a ConfigHub rule treats warnings ConfigHub returns about the reported dimensions
/// (for example <c>missing=server</c> or <c>unknown=server:APPTEST03</c>). ConfigHub still
/// delivers a configuration in these cases — without the affected layer.
/// </summary>
public enum ConfigHubWarningMode
{
    /// <summary>Log the warnings and keep using the delivered configuration (default).</summary>
    Warn,

    /// <summary>
    /// Treat warnings as a failed fetch. A <c>Required()</c> rule then fails startup; an optional
    /// rule is reported as failed and the configuration health becomes degraded.
    /// </summary>
    Fail,
}

/// <summary>
/// Thrown in <see cref="ConfigHubWarningMode.Fail"/> when ConfigHub answers with warnings
/// about the reported dimensions.
/// </summary>
public sealed class ConfigHubWarningException : Exception
{
    /// <summary>The warnings as sent by ConfigHub.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Creates the exception for the given warnings.</summary>
    public ConfigHubWarningException(IReadOnlyList<string> warnings)
        : base($"ConfigHub reported problems with the reported dimensions: {string.Join("; ", warnings)}")
    {
        Warnings = warnings;
    }
}
