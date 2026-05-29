using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.Exceptions;
using Cocoar.Configuration.X509Encryption;
using Cocoar.Configuration.Secrets.Helpers;
using Cocoar.FileSystem;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Encapsulates configuration and setup logic for Hybrid/X.509 protectors.
/// Keeps SecretsSetupDeferredConfiguration thin by delegating feature-specific work here.
/// </summary>
internal sealed class HybridProtectorConfigurator(ConfigManagerCapabilityScope capabilityScope)
{
    private readonly ConfigManagerCapabilityScope _capabilityScope = capabilityScope ?? throw new ArgumentNullException(nameof(capabilityScope));

    private void RegisterProtector(IRuntimeSecretDecryptor protector)
    {
        var composition = _capabilityScope.Owner.GetComposition();
        if (composition == null) return;

        var recomposer = _capabilityScope.Recompose(composition);
        recomposer.AddAs<IRuntimeSecretDecryptor>(protector);
        recomposer.Build();
    }

    private void RegisterProtectorAndKeyInfo(IRuntimeSecretDecryptor protector, ISecretEncryptionKeyInfoProvider keyInfo)
    {
        var composition = _capabilityScope.Owner.GetComposition();
        if (composition == null) return;

        var recomposer = _capabilityScope.Recompose(composition);
        recomposer.AddAs<IRuntimeSecretDecryptor>(protector);
        recomposer.AddAs<ISecretEncryptionKeyInfoProvider>(keyInfo);
        recomposer.Build();
    }

    /// <summary>
    /// Unified method to apply certificate protector configuration.
    /// </summary>
    public void ApplyCertificateProtector(CertificateProtectorConfig config)
    {
        // Validate structure before proceeding
        ValidateCertificateStructure(config);

        var configAccessor = _capabilityScope.Owner.Get();

        // Single-Kid Mode: All certs use one kid
        if (config.ForceSingleKid != null)
        {
            ApplySingleKidMode(config, configAccessor);
            return;
        }

        // Multi-Kid Mode: Use folder structure
        ApplyMultiKidMode(config, configAccessor);
    }

    private void ApplySingleKidMode(CertificateProtectorConfig config, IConfigurationAccessor configAccessor)
    {
        // In single-kid mode, all certificates are in a flat structure (no kid subfolders)
        // The protector will only respond to the configured kid(s)
        var passwordProvider = config.PasswordProvider ?? (config.Password != null ? _ => [config.Password] : null);

        var inventory = new CertificateInventory(
            config.BasePath,
            config.SearchPattern,
            kid: null,  // No kid filtering in inventory
            configAccessor,
            passwordProvider,
            config.CacheDurationSeconds,
            config.CertificateComparer,
            includeSubdirectories: 0);  // Flat structure, no subdirectories

        // In single-kid mode with the new architecture, we create a kid folder on the fly
        // or we need to handle this differently. Actually, let's just register for the single kid.
        var protector = new SingleKidProtectorWrapper(inventory, config.ForceSingleKid!, config.AdditionalKids);

        // Publish the current encryption public key for this single, unambiguous kid.
        // (Multi-kid / folder mode is decrypt-only here; per-tenant publishing comes with multi-tenancy.)
        var keyInfo = new InventoryKeyInfoProvider(inventory, config.ForceSingleKid!);
        RegisterProtectorAndKeyInfo(protector, keyInfo);
    }

    private void ApplyMultiKidMode(CertificateProtectorConfig config, IConfigurationAccessor configAccessor)
    {
        var passwordProvider = config.PasswordProvider ?? (config.Password != null ? _ => [config.Password] : null);

        // Single global inventory watching the entire certificate tree recursively
        var globalInventory = new CertificateInventory(
            config.BasePath,
            config.SearchPattern,
            kid: null,
            configAccessor,
            passwordProvider,
            config.CacheDurationSeconds,
            config.CertificateComparer,
            includeSubdirectories: -1);  // Unlimited recursive - watches all kid folders

        // Register ONE protector that handles all kids dynamically
        var protector = new X509HybridFolderSecretProtector(globalInventory);
        RegisterProtector(protector);
    }

    private static void ValidateCertificateStructure(CertificateProtectorConfig config)
    {
        // Skip validation for single-kid mode
        if (config.ForceSingleKid != null)
            return;

        // Find all certificates recursively
        if (!Directory.Exists(config.BasePath))
            return;

        var allCerts = Directory.GetFiles(config.BasePath, config.SearchPattern, SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (var certPath in allCerts)
        {
            var relativePath = Path.GetRelativePath(config.BasePath, certPath);
            var depth = CalculateDepth(relativePath);

            // Depth 0 = fallback (always allowed)
            // Depth 1+ = must be <= MaxKidDepth + 1 (kid folder + depth within)
            if (depth > 0 && depth > config.MaxKidDepth + 1)
            {
                violations.Add($"  • {relativePath} (depth: {depth}, max: {config.MaxKidDepth + 1})");
            }
        }

        if (violations.Count > 0)
        {
            var errorMsg = new StringBuilder();
            errorMsg.AppendLine(CultureInfo.InvariantCulture, $"Certificate folder structure violates MaxKidDepth={config.MaxKidDepth}.");
            errorMsg.AppendLine();
            errorMsg.AppendLine("The following certificates are too deeply nested:");
            errorMsg.AppendLine();
            foreach (var violation in violations)
            {
                errorMsg.AppendLine(violation);
            }
            errorMsg.AppendLine();
            errorMsg.AppendLine(CultureInfo.InvariantCulture, $"With MaxKidDepth={config.MaxKidDepth}, certificates must be:");
            errorMsg.AppendLine("  • Directly in BasePath (depth 0, fallback certificates)");
            errorMsg.AppendLine("  • In immediate child folders (depth 1, kid folders)");
            if (config.MaxKidDepth > 1)
            {
                errorMsg.AppendLine(CultureInfo.InvariantCulture, $"  • Up to depth {config.MaxKidDepth} within kid folders (depth 1+{config.MaxKidDepth} total)");
            }
            errorMsg.AppendLine();
            errorMsg.AppendLine("Expected structure:");
            errorMsg.AppendLine("  BasePath/");
            errorMsg.AppendLine("  ├── fallback.pfx     ← Depth 0 (optional fallback)");
            errorMsg.AppendLine("  ├── kid1/");
            errorMsg.AppendLine("  │   └── cert.pfx     ← Depth 1 (kid certificates)");
            errorMsg.AppendLine("  └── kid2/");
            errorMsg.AppendLine("      └── cert.pfx     ← Depth 1 (kid certificates)");

            throw new InvalidOperationException(errorMsg.ToString());
        }
    }

    private static int CalculateDepth(string relativePath)
    {
        var directoryPart = Path.GetDirectoryName(relativePath) ?? string.Empty;
        if (string.IsNullOrEmpty(directoryPart) || directoryPart == ".")
            return 0;

        var separators = directoryPart.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return separators.Length;
    }
}

/// <summary>
/// Protector wrapper for single-kid mode (flat certificate structure).
/// Accepts any of the configured kids and uses the flat certificate inventory.
/// </summary>
internal sealed class SingleKidProtectorWrapper : IRuntimeSecretEncryptor
{
    private static readonly System.Text.Json.JsonSerializerOptions EnvelopeDeserializationOptions = new()
    {
        Converters = { new Cocoar.Configuration.Secrets.Converters.Base64UrlByteArrayConverter() }
    };

    private readonly CertificateInventory _inventory;
    private readonly HashSet<string> _acceptedKids;

    public SingleKidProtectorWrapper(CertificateInventory inventory, string primaryKid, string[]? additionalKids)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _acceptedKids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryKid };

        if (additionalKids != null)
        {
            foreach (var kid in additionalKids)
                _acceptedKids.Add(kid);
        }
    }

    public bool CanDecrypt(string kid)
    {
        // Accept any of the configured kids
        return _acceptedKids.Contains(kid);
    }

    public byte[] UnprotectInternal(IEncryptedEnvelope envelope, string kid)
    {
        if (!CanDecrypt(kid))
            throw new InvalidOperationException($"This protector does not handle kid '{kid}'");

        var hybridEnv = (HybridEnvelope)envelope;

        // Use the flat inventory (no kid subfolder - all certs in root)
        if (_inventory.TryDecrypt(hybridEnv, out var plaintext))
            return plaintext;

        // Detailed error with actionable guidance
        throw SecretDecryptionException.DecryptionFailed(
            kid,
            "RSA-OAEP-AES256-GCM",
            new System.Security.Cryptography.CryptographicException($"No certificate could decrypt this envelope for kid '{kid}'"));
    }

    public IEncryptedEnvelope ProtectInternal(ReadOnlySpan<byte> plaintext, string kid)
    {
        throw new NotSupportedException("Single-kid mode does not support encryption. Use UseCertificateFromFile with an encryption cert.");
    }

    public IEncryptedEnvelope DeserializeEnvelope(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<HybridEnvelope>(json, EnvelopeDeserializationOptions)!;
    }
}
