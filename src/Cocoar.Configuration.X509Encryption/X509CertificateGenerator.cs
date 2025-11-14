using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.Configuration.X509Encryption;

/// <summary>
/// Generates self-signed X.509 certificates for encryption purposes.
/// </summary>
public static class X509CertificateGenerator
{
    /// <summary>
    /// Generates a self-signed certificate.
    /// </summary>
    /// <param name="subject">Certificate subject (e.g., "CN=MyApp").</param>
    /// <param name="validYears">Validity period in years (default: 1).</param>
    /// <param name="keySize">RSA key size in bits: 2048, 3072, or 4096 (default: 2048).</param>
    /// <returns>A new self-signed X509Certificate2 with private key.</returns>
    /// <exception cref="ArgumentException">If keySize is not 2048, 3072, or 4096.</exception>
    public static X509Certificate2 GenerateSelfSigned(
        string subject,
        int validYears = 1,
        int keySize = 2048)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        
        if (keySize != 2048 && keySize != 3072 && keySize != 4096)
            throw new ArgumentException($"Key size must be 2048, 3072, or 4096, got {keySize}", nameof(keySize));

        if (validYears < 1)
            throw new ArgumentException("Validity period must be at least 1 year", nameof(validYears));

        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest(
            subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add basic constraints
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: false));

        // Add key usage
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment,
                critical: false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(validYears);

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>
    /// Generates a self-signed certificate and saves it as a PFX file.
    /// </summary>
    /// <param name="outputPath">Path where the PFX file will be saved.</param>
    /// <param name="password">Password to protect the PFX file.</param>
    /// <param name="subject">Certificate subject (e.g., "CN=MyApp").</param>
    /// <param name="validYears">Validity period in years (default: 1).</param>
    /// <param name="keySize">RSA key size in bits: 2048, 3072, or 4096 (default: 2048).</param>
    /// <param name="overwrite">If true, overwrites existing file; otherwise throws if file exists.</param>
    /// <returns>The generated certificate.</returns>
    /// <exception cref="IOException">If file exists and overwrite is false.</exception>
    public static X509Certificate2 GenerateAndSavePfx(
        string outputPath,
        string password,
        string subject,
        int validYears = 1,
        int keySize = 2048,
        bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (File.Exists(outputPath) && !overwrite)
            throw new IOException($"File already exists: {outputPath}. Use overwrite=true to replace.");

        var cert = GenerateSelfSigned(subject, validYears, keySize);

        try
        {
            var pfxBytes = cert.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(outputPath, pfxBytes);
            return cert;
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Generates a self-signed certificate and saves it in PEM format (.crt + .key files).
    /// </summary>
    /// <param name="certPath">Path where the certificate file will be saved (.crt).</param>
    /// <param name="keyPath">Path where the private key file will be saved (.key). If null, uses certPath with .key extension.</param>
    /// <param name="subject">Certificate subject (e.g., "CN=MyApp").</param>
    /// <param name="validYears">Validity period in years (default: 1).</param>
    /// <param name="keySize">RSA key size in bits: 2048, 3072, or 4096 (default: 2048).</param>
    /// <param name="overwrite">If true, overwrites existing files; otherwise throws if files exist.</param>
    /// <returns>The generated certificate (without private key - use keyPath to load full cert).</returns>
    /// <exception cref="IOException">If files exist and overwrite is false.</exception>
    public static X509Certificate2 GenerateAndSavePem(
        string certPath,
        string? keyPath = null,
        string subject = "CN=Cocoar Secrets",
        int validYears = 1,
        int keySize = 2048,
        bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certPath);

        keyPath ??= Path.ChangeExtension(certPath, ".key");

        if (File.Exists(certPath) && !overwrite)
            throw new IOException($"Certificate file already exists: {certPath}. Use overwrite=true to replace.");

        if (File.Exists(keyPath) && !overwrite)
            throw new IOException($"Private key file already exists: {keyPath}. Use overwrite=true to replace.");

        var cert = GenerateSelfSigned(subject, validYears, keySize);

        try
        {
            // Export certificate (public key only) as PEM
            var certPem = cert.ExportCertificatePem();
            File.WriteAllText(certPath, certPem);

            // Export private key as PEM
            var key = cert.GetRSAPrivateKey();
            if (key == null)
                throw new InvalidOperationException("Certificate does not contain an RSA private key.");

            var keyPem = key.ExportRSAPrivateKeyPem();
            File.WriteAllText(keyPath, keyPem);

            return cert;
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Generates a self-signed certificate and saves it as a PFX file.
    /// </summary>
    /// <param name="outputPath">Path where the certificate will be saved.</param>
    /// <param name="password">Password for PFX file.</param>
    /// <param name="subject">Certificate subject (e.g., "CN=MyApp").</param>
    /// <param name="validYears">Validity period in years (default: 1).</param>
    /// <param name="keySize">RSA key size in bits: 2048, 3072, or 4096 (default: 2048).</param>
    /// <param name="overwrite">If true, overwrites existing files; otherwise throws if files exist.</param>
    /// <returns>The generated certificate.</returns>
    [Obsolete("Use GenerateAndSavePfx or GenerateAndSavePem instead.")]
    public static X509Certificate2 GenerateAndSave(
        string outputPath,
        string password,
        string subject,
        int validYears = 1,
        int keySize = 2048,
        bool overwrite = false)
    {
        return GenerateAndSavePfx(outputPath, password, subject, validYears, keySize, overwrite);
    }

    /// <summary>
    /// Converts a certificate from PFX to PEM format.
    /// </summary>
    /// <param name="pfxPath">Path to input PFX file.</param>
    /// <param name="pfxPassword">Password for PFX file.</param>
    /// <param name="certPath">Path where certificate will be saved (.crt).</param>
    /// <param name="keyPath">Path where private key will be saved (.key). If null, uses certPath with .key extension.</param>
    /// <param name="overwrite">If true, overwrites existing files; otherwise throws if files exist.</param>
    /// <returns>The certificate (without private key - use keyPath to load full cert).</returns>
    /// <exception cref="IOException">If files exist and overwrite is false.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">If PFX cannot be loaded or password is incorrect.</exception>
    public static X509Certificate2 ConvertPfxToPem(
        string pfxPath,
        string pfxPassword,
        string certPath,
        string? keyPath = null,
        bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(certPath);

        keyPath ??= Path.ChangeExtension(certPath, ".key");

        if (File.Exists(certPath) && !overwrite)
            throw new IOException($"Certificate file already exists: {certPath}. Use overwrite=true to replace.");

        if (File.Exists(keyPath) && !overwrite)
            throw new IOException($"Private key file already exists: {keyPath}. Use overwrite=true to replace.");

        // Load PFX with private key
        var cert = new X509Certificate2(pfxPath, pfxPassword, X509KeyStorageFlags.Exportable);

        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new InvalidOperationException("PFX file does not contain a private key.");
        }

        try
        {
            // Export certificate (public key only)
            var certPem = cert.ExportCertificatePem();
            File.WriteAllText(certPath, certPem);

            // Export private key
            var key = cert.GetRSAPrivateKey();
            if (key == null)
            {
                cert.Dispose();
                throw new InvalidOperationException("Certificate does not contain an RSA private key.");
            }

            var keyPem = key.ExportRSAPrivateKeyPem();
            File.WriteAllText(keyPath, keyPem);

            return cert;
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Converts a certificate from PEM to PFX format.
    /// </summary>
    /// <param name="certPath">Path to input certificate file (.crt, .pem, .cer).</param>
    /// <param name="keyPath">Path to private key file (.key). If null, uses certPath with .key extension.</param>
    /// <param name="pfxPath">Path where PFX file will be saved.</param>
    /// <param name="pfxPassword">Password for output PFX file.</param>
    /// <param name="overwrite">If true, overwrites existing file; otherwise throws if file exists.</param>
    /// <returns>The certificate with private key.</returns>
    /// <exception cref="FileNotFoundException">If certificate or key file not found.</exception>
    /// <exception cref="IOException">If output file exists and overwrite is false.</exception>
    public static X509Certificate2 ConvertPemToPfx(
        string certPath,
        string? keyPath,
        string pfxPath,
        string pfxPassword,
        bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxPassword);

        keyPath ??= Path.ChangeExtension(certPath, ".key");

        if (!File.Exists(certPath))
            throw new FileNotFoundException($"Certificate file not found: {certPath}");

        if (!File.Exists(keyPath))
            throw new FileNotFoundException($"Private key file not found: {keyPath}");

        if (File.Exists(pfxPath) && !overwrite)
            throw new IOException($"PFX file already exists: {pfxPath}. Use overwrite=true to replace.");

        // Load PEM certificate with private key
        var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new InvalidOperationException("Certificate does not contain a private key.");
        }

        try
        {
            // Export as PFX
            var pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);
            File.WriteAllBytes(pfxPath, pfxBytes);

            return cert;
        }
        catch
        {
            cert.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Certificate output format.
/// </summary>
public enum CertificateFormat
{
    /// <summary>
    /// PKCS#12 format (.pfx, .p12) - certificate + private key in single password-protected file.
    /// </summary>
    Pfx,
    
    /// <summary>
    /// PEM format (.crt + .key) - certificate and private key in separate text files.
    /// </summary>
    Pem,
    
    /// <summary>
    /// Auto-detect from file extension (.pfx/.p12 = Pfx, .crt/.pem/.cer = Pem).
    /// </summary>
    Auto
}
