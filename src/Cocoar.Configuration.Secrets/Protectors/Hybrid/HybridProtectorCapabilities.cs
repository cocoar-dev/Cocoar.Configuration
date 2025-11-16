using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Unified configuration for certificate-based hybrid encryption.
/// Supports both single-kid (flat) and multi-kid (folder-based) scenarios.
/// </summary>
public sealed record CertificateProtectorConfig : IProtectorConfiguration
{
    /// <summary>
    /// Base path for certificates. Can be a folder or specific file path.
    /// </summary>
    public required string BasePath { get; init; }
    
    /// <summary>
    /// Search pattern for certificate files (e.g., "*.pfx", "cert.pfx").
    /// </summary>
    public string SearchPattern { get; init; } = "*.pfx";
    
    /// <summary>
    /// If set, forces all certificates to use this Kid, ignoring folder structure.
    /// Use for single-kid scenarios or flat folder structures.
    /// If null, uses folder names as Kids (multi-kid mode).
    /// </summary>
    public string? ForceSingleKid { get; init; }
    
    /// <summary>
    /// Additional Kid aliases for the primary kid (only used with ForceSingleKid).
    /// </summary>
    public string[]? AdditionalKids { get; init; }
    
    /// <summary>
    /// Simple password for all certificates (alternative to PasswordProvider).
    /// </summary>
    public string? Password { get; init; }
    
    /// <summary>
    /// Function to provide passwords for certificates based on context.
    /// Takes precedence over simple Password property.
    /// </summary>
    public Func<CertificateContext, string[]>? PasswordProvider { get; init; }
    
    /// <summary>
    /// Duration to cache loaded certificates in seconds.
    /// Certificates are automatically monitored for changes and reloaded.
    /// </summary>
    public int CacheDurationSeconds { get; init; } = 30;
    
    /// <summary>
    /// Custom comparer for sorting certificates (e.g., prefer newer certs).
    /// </summary>
    public IComparer<FileInfo>? CertificateComparer { get; init; }
    
    /// <summary>
    /// Maximum depth for kid folders relative to BasePath.
    /// Only applies when ForceSingleKid is null (multi-kid mode).
    /// - Depth 0: Files only in BasePath (no kid folders, requires ForceSingleKid)
    /// - Depth 1: Kid folders are immediate children (default, recommended)
    /// - Depth 2+: Allows nested kid structures (advanced scenarios)
    /// Certificates can be at any depth within a kid folder.
    /// </summary>
    public int MaxKidDepth { get; init; } = 1;
}

