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

    public static CertificateSetupBuilder UseCertificateFromFile(this SecretsBuilder builder, string pfxPath, string? password = null)
    {
        EnsureContributorRegistered(SecretsBuilder.GetComposerFor(builder));
        return new(SecretsBuilder.GetCapabilityScopeFor(builder), builder, SecretsBuilder.GetComposerFor(builder), pfxPath, password);
    }

    /// <summary>
    /// Registers certificates from a folder for hybrid encryption/decryption.
    /// Supports multiple certificate formats:
    /// - PKCS#12: .pfx, .p12 (password-protected archives)
    /// - PEM: .pem, .crt, .cer (requires matching .key file with same base name)
    /// </summary>
    /// <param name="builder">The secrets builder.</param>
    /// <param name="basePath">Path to folder containing certificates.</param>
    /// <param name="passwordProvider">Optional function to provide passwords for encrypted certificates (PKCS#12 only).</param>
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
    public static SecretsBuilder UseCertificatesFromFolder(
        this SecretsBuilder builder,
        string basePath,
        Func<CertificateContext, string[]>? passwordProvider = null,
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
            PasswordProvider = passwordProvider,
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
    private readonly string? _password;
    private string _keyId = "hybrid-encryption";
    private readonly List<string> _additionalKids = new();

    internal CertificateSetupBuilder(ConfigManagerCapabilityScope capabilityScope, SecretsBuilder setup, Composer? composer, string pfxPath, string? password) : base(capabilityScope)
    {
        _setup = setup;
        _composer = composer;
        _pfxPath = pfxPath;
        _password = password;
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
        _composer?.Add(new CertificateProtectorConfig
        {
            BasePath = Path.GetDirectoryName(_pfxPath) ?? ".",
            SearchPattern = Path.GetFileName(_pfxPath) ?? "*.pfx",
            ForceSingleKid = _keyId,
            AdditionalKids = _additionalKids.Count > 0 ? _additionalKids.ToArray() : null,
            Password = _password
        });
        return _setup;
    }
}
