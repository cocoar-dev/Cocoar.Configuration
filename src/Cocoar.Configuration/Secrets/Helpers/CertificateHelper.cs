using System.Security.Cryptography.X509Certificates;

namespace Cocoar.Configuration.Secrets.Helpers;

/// <summary>
/// Helper methods for loading and querying X.509 certificates.
/// Certificate generation is available via <see cref="X509Encryption.X509CertificateGenerator"/>.
/// </summary>
public static class CertificateHelper
{
    public static X509Certificate2 LoadFromFile(
        string path,
        string? password = null,
        X509KeyStorageFlags? keyStorageFlags = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Certificate file not found: {path}", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var flags = keyStorageFlags ?? GetPlatformDefaultKeyStorageFlags();

        X509Certificate2 cert = extension switch
        {
            ".pfx" or ".p12" => LoadPkcs12Certificate(path, password, flags),
            ".pem" or ".crt" or ".cer" => LoadPemCertificate(path, flags),
            _ => throw new NotSupportedException(
                $"Certificate format '{extension}' is not supported. " +
                $"Supported formats: .pfx, .p12 (PKCS#12), .pem, .crt, .cer (PEM with matching .key file)")
        };

        // Validate certificate expiration
        ValidateCertificateExpiration(cert, path);

        // Validate certificate security properties
        ValidateCertificateSecurity(cert, path);

        if (!cert.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"The certificate at '{path}' does not contain a private key. " +
                $"For PEM certificates, ensure a matching private key file exists (e.g., {Path.ChangeExtension(path, ".key")}).");
        }

        return cert;
    }

    private static X509Certificate2 LoadPkcs12Certificate(string path, string? password, X509KeyStorageFlags flags)
    {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
#else
        return new X509Certificate2(path, password, flags);
#endif
    }

    private static X509Certificate2 LoadPemCertificate(string certPath, X509KeyStorageFlags flags)
    {
        // PEM certificates require a separate private key file
        var directory = Path.GetDirectoryName(certPath) ?? ".";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(certPath);

        // Look for matching .key file with same base name
        var keyPath = Path.Combine(directory, $"{fileNameWithoutExt}.key");

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException(
                $"PEM certificate requires a matching private key file. Expected: {keyPath}",
                keyPath);
        }

        // Load certificate and private key from PEM files
        var certPem = File.ReadAllText(certPath);
        var keyPem = File.ReadAllText(keyPath);

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private static X509KeyStorageFlags GetPlatformDefaultKeyStorageFlags()
    {
        if (OperatingSystem.IsMacOS())
        {
            // macOS requires PersistKeySet due to keychain behavior
            return X509KeyStorageFlags.PersistKeySet;
        }

        if (OperatingSystem.IsLinux())
        {
            // Linux prefers MachineKeySet for system-wide certs
            return X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet;
        }

        // Windows default - ephemeral for security
        return X509KeyStorageFlags.EphemeralKeySet;
    }

    public static X509Certificate2 FindByThumbprint(
        string thumbprint,
        StoreName storeName = StoreName.My,
        StoreLocation storeLocation = StoreLocation.CurrentUser)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new ArgumentException("Thumbprint is required", nameof(thumbprint));

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        var cert = matches.Cast<X509Certificate2?>().FirstOrDefault(c => c is not null && c.HasPrivateKey);

        if (cert is null)
        {
            throw new InvalidOperationException(
                $"Certificate with thumbprint '{thumbprint}' not found or missing private key in {storeLocation}/{storeName}.");
        }

        return cert;
    }

    public static X509Certificate2 FindBySubject(
        string subjectDistinguishedName,
        StoreName storeName = StoreName.My,
        StoreLocation storeLocation = StoreLocation.CurrentUser)
    {
        if (string.IsNullOrWhiteSpace(subjectDistinguishedName))
            throw new ArgumentException("Subject distinguished name is required", nameof(subjectDistinguishedName));

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(
            X509FindType.FindBySubjectDistinguishedName,
            subjectDistinguishedName,
            validOnly: false);

        var cert = matches.Cast<X509Certificate2?>().FirstOrDefault(c => c is not null && c.HasPrivateKey);

        if (cert is null)
        {
            throw new InvalidOperationException(
                $"Certificate with subject '{subjectDistinguishedName}' not found or missing private key in {storeLocation}/{storeName}.");
        }

        return cert;
    }

    private static void ValidateCertificateExpiration(X509Certificate2 cert, string path)
    {
        var now = DateTime.UtcNow;

        // Warn if already expired
        if (cert.NotAfter < now)
        {
            var daysExpired = (now - cert.NotAfter).Days;
            Console.WriteLine(
                $"WARNING: Certificate '{cert.Subject}' EXPIRED {daysExpired} days ago " +
                $"(Expired: {cert.NotAfter:yyyy-MM-dd}). Rotation urgently needed. " +
                $"Path: {path}, Thumbprint: {cert.Thumbprint}");
        }
        // Warn if expiring within 30 days
        else if ((cert.NotAfter - now).TotalDays < 30)
        {
            var daysRemaining = (int)(cert.NotAfter - now).TotalDays;
            Console.WriteLine(
                $"WARNING: Certificate '{cert.Subject}' expires in {daysRemaining} days " +
                $"(Expires: {cert.NotAfter:yyyy-MM-dd}). Plan rotation soon. " +
                $"Path: {path}, Thumbprint: {cert.Thumbprint}");
        }
    }

    private static void ValidateCertificateSecurity(X509Certificate2 cert, string path)
    {
        // Check RSA key size
        var rsa = cert.GetRSAPublicKey();
        if (rsa != null)
        {
            var keySize = rsa.KeySize;
            if (keySize < 2048)
            {
                Console.WriteLine(
                    $"WARNING: Certificate '{cert.Subject}' uses weak RSA key size {keySize} bits. " +
                    $"Recommended: 2048+ bits for security. " +
                    $"Path: {path}, Thumbprint: {cert.Thumbprint}");
            }
        }

        // Check signature algorithm for weak hashes
        var sigAlgOid = cert.SignatureAlgorithm.Value;
        if (sigAlgOid != null)
        {
            // OIDs for weak algorithms: MD5, SHA-1
            // MD5: 1.2.840.113549.1.1.4 (md5WithRSAEncryption)
            // SHA-1: 1.2.840.113549.1.1.5 (sha1WithRSAEncryption), 1.3.14.3.2.29 (sha1WithRSA)
            if (sigAlgOid.Contains("1.1.4") || // md5WithRSAEncryption
                sigAlgOid.Contains("1.1.5") || // sha1WithRSAEncryption
                sigAlgOid.Contains("3.2.29"))  // sha1WithRSA
            {
                Console.WriteLine(
                    $"WARNING: Certificate '{cert.Subject}' uses weak signature algorithm " +
                    $"'{cert.SignatureAlgorithm.FriendlyName}'. " +
                    $"Recommended: SHA-256 or higher. " +
                    $"Path: {path}, Thumbprint: {cert.Thumbprint}");
            }
        }
    }
}

