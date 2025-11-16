using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Protectors.Hybrid;


namespace Cocoar.Configuration.Secrets;

public static class SecretsHybridExtensions
{
    private static void EnsureContributorRegistered(Composer? composer)
    {
        if (composer != null && !composer.Has<HybridProtectorSetupContributor>())
        {
            composer.AddAs<ISecretsSetupContributor>(new HybridProtectorSetupContributor());
        }
    }

    /// <summary>
    /// Registers a single certificate file for hybrid encryption/decryption.
    /// Certificate must be password-less and protected by file system permissions.
    /// </summary>
    /// <param name="builder">The secrets builder.</param>
    /// <param name="pfxPath">Path to the certificate file (PFX or PEM).</param>
    /// <remarks>
    /// Best practice: Use password-less certificates protected by file permissions (chmod 600 on Linux/macOS, ACLs on Windows).
    /// If you have a password-protected certificate, use 'cocoar-secrets convert-cert' CLI command to convert it.
    /// </remarks>
    public static CertificateSetupBuilder UseCertificateFromFile(this SecretsBuilder builder, string pfxPath)
    {
        EnsureContributorRegistered(SecretsBuilder.GetComposerFor(builder));
        return new(SecretsBuilder.GetCapabilityScopeFor(builder), builder, SecretsBuilder.GetComposerFor(builder), pfxPath);
    }

    /// <summary>
    /// Registers certificates from a folder for hybrid encryption/decryption.
    /// Supports multiple certificate formats:
    /// - PKCS#12: .pfx, .p12 (password-less archives)
    /// - PEM: .pem, .crt, .cer (requires matching .key file with same base name)
    /// 
    /// All certificates must be password-less and protected by file system permissions.
    /// Supports kid-based subdirectories for multi-tenant scenarios: basePath/{kid}/cert.pfx
    /// </summary>
    /// <param name="builder">The secrets builder.</param>
    /// <param name="basePath">Path to folder containing certificates.</param>
    /// <param name="searchPattern">
    /// File pattern to search for certificates. Default "*" searches all supported formats.
    /// Examples:
    /// - "*" (default) - Auto-discover all formats (*.pfx, *.p12, *.pem, *.crt, *.cer)
    /// - "*.pfx" - Only PFX files
    /// - "*.pfx;*.p12" - Multiple patterns (semicolon-separated)
    /// - "*.crt" - Only PEM certificates with matching .key files
    /// Note: PEM certificates (.crt, .cer, .pem) require a matching .key file with the same base name.
    /// </param>
    /// <param name="cacheDurationSeconds">How long to cache loaded certificates (default: 30 seconds).</param>
    /// <param name="certificateComparer">Optional comparer for certificate selection order.</param>
    /// <remarks>
    /// Best practice: Use password-less certificates protected by file permissions.
    /// - Linux/macOS: chmod 600 cert.pfx &amp;&amp; chown app-user cert.pfx
    /// - Windows: icacls cert.pfx /inheritance:r /grant:r "AppUser:(R)"
    /// 
    /// If you have password-protected certificates, use 'cocoar-secrets convert-cert' CLI command to convert them.
    /// </remarks>
    public static SecretsBuilder UseCertificatesFromFolder(
        this SecretsBuilder builder,
        string basePath,
        string searchPattern = "*",
        int cacheDurationSeconds = 30,
        IComparer<FileInfo>? certificateComparer = null)
    {
        EnsureContributorRegistered(SecretsBuilder.GetComposerFor(builder));
        var composer = SecretsBuilder.GetComposerFor(builder);
        
        composer?.Add(new CertificateProtectorConfig
        {
            BasePath = basePath,
            SearchPattern = searchPattern,
            ForceSingleKid = null,  // Multi-Kid mode
            PasswordProvider = null,  // Password-less only
            CacheDurationSeconds = cacheDurationSeconds,
            CertificateComparer = certificateComparer
        });

        return builder;
    }
}

public sealed class CertificateSetupBuilder: SetupDefinition
{
    private readonly SecretsBuilder _setup;
    private readonly Composer? _composer;
    private readonly string _pfxPath;
    private string _keyId = "hybrid-encryption";
    private readonly List<string> _additionalKids = new();

    internal CertificateSetupBuilder(ConfigManagerCapabilityScope capabilityScope, SecretsBuilder setup, Composer? composer, string pfxPath) : base(capabilityScope)
    {
        _setup = setup;
        _composer = composer;
        _pfxPath = pfxPath;
    }

    public CertificateSetupBuilder WithKeyId(string keyId)
    {
        _keyId = keyId;
        return this;
    }

    public CertificateSetupBuilder WithAdditionalKeyId(string additionalKeyId)
    {
        if (!_additionalKids.Contains(additionalKeyId))
        {
            _additionalKids.Add(additionalKeyId);
        }
        return this;
    }

    internal override SetupDefinition Build()
    {
        // Resolve path relative to application directory
        var fullPath = Path.IsPathRooted(_pfxPath)
            ? _pfxPath
            : Path.Combine(AppContext.BaseDirectory, _pfxPath);
        
        _composer?.Add(new CertificateProtectorConfig
        {
            BasePath = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory,
            SearchPattern = Path.GetFileName(fullPath) ?? "*.pfx",
            ForceSingleKid = _keyId,
            AdditionalKids = _additionalKids.Count > 0 ? _additionalKids.ToArray() : null,
            Password = null  // Password-less only
        });
        return _setup;
    }
}
