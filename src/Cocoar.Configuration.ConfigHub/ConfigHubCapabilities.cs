namespace Cocoar.Configuration.ConfigHub;

/// <summary>Versioned upstream functions currently accepted by a ConfigHub access.</summary>
public sealed record ConfigHubCapabilities(int ProtocolVersion, IReadOnlyList<string> Features)
{
    /// <summary>Schema registration feature identifier.</summary>
    public const string Schema = "schema";
    /// <summary>Public encryption-key registration feature identifier.</summary>
    public const string EncryptionKey = "encryption-key";
    /// <summary>Whether this protocol version advertises the given function.</summary>
    public bool Supports(string feature) => ProtocolVersion == 1 && Features is not null && Features.Contains(feature, StringComparer.Ordinal);
}
