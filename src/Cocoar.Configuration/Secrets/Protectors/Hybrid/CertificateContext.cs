using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Provides context information about a certificate being loaded.
/// Used by password providers to determine the appropriate password(s) to try.
/// </summary>
public sealed class CertificateContext
{
    /// <summary>
    /// The configuration accessor for reading passwords and other configuration values.
    /// Provides access to the entire configuration system at the time the certificate is loaded.
    /// </summary>
    public required IConfigurationAccessor Config { get; init; }

    /// <summary>
    /// The full file path of the certificate.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The key identifier (kid) associated with this certificate.
    /// For kid-specific certificates, this is the folder name.
    /// For global fallback certificates, this is null.
    /// </summary>
    public string? Kid { get; init; }
}
