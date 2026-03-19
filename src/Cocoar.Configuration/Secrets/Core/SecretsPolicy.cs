namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Configuration policy for secrets deserialization behavior.
/// </summary>
public sealed record SecretsPolicy
{
    /// <summary>
    /// Gets the default secrets policy with all security protections enabled.
    /// </summary>
    public static SecretsPolicy Default { get; } = new();

    /// <summary>
    /// When true, allows Secret&lt;T&gt; to be deserialized from plaintext JSON values
    /// without throwing on <c>.Open()</c>.
    /// <para>
    /// <strong>SECURITY WARNING:</strong> Only enable this in development/test environments.
    /// Production configurations should always use encrypted envelopes.
    /// </para>
    /// </summary>
    public bool AllowPlaintextSecrets { get; init; }
}
