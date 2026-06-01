using System;

namespace Cocoar.Configuration.Secrets.Exceptions;

/// <summary>
/// Exception thrown when secret decryption fails with detailed diagnostic context.
/// </summary>
public class SecretDecryptionException : InvalidOperationException
{
    /// <summary>
    /// The Key ID (kid) that was attempted for decryption.
    /// </summary>
    public string? AttemptedKid { get; }

    /// <summary>
    /// The encryption algorithm from the envelope.
    /// </summary>
    public string? Algorithm { get; }

    /// <summary>
    /// List of Key IDs that are currently available in the protector registry.
    /// </summary>
    public string[]? AvailableKids { get; }

    /// <summary>
    /// Creates a new SecretDecryptionException with detailed context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="attemptedKid">The kid that was attempted.</param>
    /// <param name="algorithm">The algorithm from the envelope.</param>
    /// <param name="availableKids">List of available kids.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SecretDecryptionException(
        string message,
        string? attemptedKid = null,
        string? algorithm = null,
        string[]? availableKids = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        AttemptedKid = attemptedKid;
        Algorithm = algorithm;
        AvailableKids = availableKids;
    }

    /// <summary>
    /// Creates a detailed error message for kid mismatch scenarios.
    /// </summary>
    public static SecretDecryptionException KidNotFound(
        string attemptedKid,
        string algorithm,
        string[] availableKids)
    {
        var message = $"Failed to decrypt secret with kid '{attemptedKid}'.\n\n" +
                     $"Possible causes:\n" +
                     $"  1. Certificate with kid '{attemptedKid}' not registered\n" +
                     $"  2. Certificate was removed or rotated without updating secrets\n" +
                     $"  3. Wrong environment (dev cert used in production)\n\n" +
                     $"Envelope algorithm: {algorithm}\n" +
                     $"Available kids: {(availableKids.Length > 0 ? string.Join(", ", availableKids) : "(none)")}\n\n" +
                     $"To fix:\n" +
                     $"  • Register the missing certificate via .UseCertificateFromFile() or .UseCertificatesFromFolder()\n" +
                     $"  • Re-encrypt secrets with an available kid using: cocoar-secrets rotate";

        return new SecretDecryptionException(message, attemptedKid, algorithm, availableKids);
    }

    /// <summary>
    /// Creates a detailed error message for decryption failures.
    /// </summary>
    public static SecretDecryptionException DecryptionFailed(
        string attemptedKid,
        string algorithm,
        Exception innerException)
    {
        var message = $"Failed to decrypt secret with kid '{attemptedKid}'.\n\n" +
                     $"Possible causes:\n" +
                     $"  1. Certificate password incorrect\n" +
                     $"  2. Certificate lacks private key\n" +
                     $"  3. Envelope data corrupted or tampered\n" +
                     $"  4. Wrong certificate (thumbprint mismatch)\n\n" +
                     $"Envelope algorithm: {algorithm}\n" +
                     $"Inner error: {innerException.Message}\n\n" +
                     $"To fix:\n" +
                     $"  • Verify certificate password is correct\n" +
                     $"  • Check certificate has private key: openssl pkcs12 -info -in cert.pfx\n" +
                     $"  • Verify envelope was encrypted with this certificate's public key";

        return new SecretDecryptionException(message, attemptedKid, algorithm, null, innerException);
    }

    /// <summary>
    /// Creates a detailed error message for invalid envelope format.
    /// </summary>
    public static SecretDecryptionException InvalidEnvelope(string reason)
    {
        var message = $"Invalid secret envelope format.\n\n" +
                     $"Reason: {reason}\n\n" +
                     $"Expected envelope structure:\n" +
                     $"  {{\n" +
                     $"    \"__cocoar_secret__\": \"v1\",\n" +
                     $"    \"kid\": \"certificate-key-id\",\n" +
                     $"    \"alg\": \"RSA-OAEP-AES256-GCM\",\n" +
                     $"    \"type\": \"utf8\",\n" +
                     $"    \"iv\": \"base64-encoded-iv\",\n" +
                     $"    \"ct\": \"base64-encoded-ciphertext\",\n" +
                     $"    \"tag\": \"base64-encoded-tag\",\n" +
                     $"    \"wk\": \"base64-encoded-wrapped-key\"\n" +
                     $"  }}\n\n" +
                     $"To fix:\n" +
                     $"  • Verify envelope was created with: cocoar-secrets encrypt\n" +
                     $"  • Check for manual JSON editing errors";

        return new SecretDecryptionException(message);
    }
}
