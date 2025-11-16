using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cocoar.Configuration.X509Encryption;

/// <summary>
/// Provides methods for encrypting and decrypting secrets directly in JSON configuration files.
/// Useful for CLI tools, PowerShell modules, and test scenarios.
/// </summary>
public static class JsonSecretsEditor
{
    /// <summary>
    /// Encrypts a value and sets it at the specified property path in a JSON file.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON configuration file</param>
    /// <param name="propertyPath">Property path using colon separator (e.g., "Database:ConnectionString")</param>
    /// <param name="plaintext">The plaintext value to encrypt</param>
    /// <param name="certificate">X.509 certificate with public key for encryption</param>
    /// <param name="kid">Key identifier (kid) for the certificate</param>
    /// <param name="createIfNotExists">If true, creates the file and directories if they don't exist</param>
    /// <returns>True if file was created, false if it already existed</returns>
    /// <exception cref="FileNotFoundException">If file doesn't exist and createIfNotExists is false</exception>
    /// <exception cref="InvalidOperationException">If JSON file doesn't contain a root object</exception>
    public static async Task<bool> EncryptValueInFileAsync(
        string jsonFilePath,
        string propertyPath,
        string plaintext,
        X509Certificate2 certificate,
        string kid = "default",
        bool createIfNotExists = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        var fileExists = File.Exists(jsonFilePath);

        // Validate file exists or creation is allowed
        if (!fileExists && !createIfNotExists)
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}. Enable createIfNotExists to create a new file.");

        // Encrypt the value
        var crypto = new X509HybridCrypto(certificate);
        var envelope = crypto.Encrypt(plaintext);

        // Read or create JSON file
        JsonObject rootObject;
        if (fileExists)
        {
            var jsonText = await File.ReadAllTextAsync(jsonFilePath);
            var jsonNode = JsonNode.Parse(jsonText);

            if (jsonNode is not JsonObject obj)
                throw new InvalidOperationException("JSON file must contain a root object");

            rootObject = obj;
        }
        else
        {
            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(jsonFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Start with an empty JSON object
            rootObject = new JsonObject();
        }

        // Set the encrypted value at the property path
        SetValueAtPath(rootObject, propertyPath, envelope, kid);

        // Write back to file with nice formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var updatedJson = JsonSerializer.Serialize(rootObject, options);
        await File.WriteAllTextAsync(jsonFilePath, updatedJson, Encoding.UTF8);

        return !fileExists; // Return true if we created the file
    }

    /// <summary>
    /// Encrypts an existing plaintext value in-place at the specified property path in a JSON file.
    /// Reads the current value, encrypts it, and replaces it with the encrypted envelope.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON configuration file</param>
    /// <param name="propertyPath">Property path using colon separator (e.g., "Database:ConnectionString")</param>
    /// <param name="certificate">X.509 certificate with public key for encryption</param>
    /// <param name="kid">Key identifier (kid) for the certificate</param>
    /// <returns>Always returns false (file must exist)</returns>
    /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If JSON file doesn't contain a root object or property path doesn't exist</exception>
    public static async Task<bool> EncryptExistingValueInFileAsync(
        string jsonFilePath,
        string propertyPath,
        X509Certificate2 certificate,
        string kid = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");

        // Read JSON file
        var jsonText = await File.ReadAllTextAsync(jsonFilePath);
        var jsonNode = JsonNode.Parse(jsonText);

        if (jsonNode is not JsonObject rootObject)
            throw new InvalidOperationException("JSON file must contain a root object");

        // Get the existing value at the path
        var existingValue = GetValueAtPath(rootObject, propertyPath);
        if (existingValue is null)
            throw new InvalidOperationException($"No value found at path: {propertyPath}");

        // Serialize the existing JSON value to a string (preserving its JSON representation)
        var jsonValueString = JsonSerializer.Serialize(existingValue);

        // Encrypt the JSON string
        var crypto = new X509HybridCrypto(certificate);
        var envelope = crypto.Encrypt(jsonValueString);

        // Replace the value with the encrypted envelope
        SetValueAtPath(rootObject, propertyPath, envelope, kid);

        // Write back to file
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var updatedJson = JsonSerializer.Serialize(rootObject, options);
        await File.WriteAllTextAsync(jsonFilePath, updatedJson, Encoding.UTF8);

        return false; // File already existed
    }

    /// <summary>
    /// Decrypts a value from the specified property path in a JSON file.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON configuration file</param>
    /// <param name="propertyPath">Property path using colon separator (e.g., "Database:ConnectionString")</param>
    /// <param name="certificate">X.509 certificate with private key for decryption</param>
    /// <returns>The decrypted plaintext value</returns>
    /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If JSON file doesn't contain a root object or property path is invalid</exception>
    public static async Task<string> DecryptValueFromFileAsync(
        string jsonFilePath,
        string propertyPath,
        X509Certificate2 certificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        ArgumentNullException.ThrowIfNull(certificate);

        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");

        // Read JSON file
        var jsonText = await File.ReadAllTextAsync(jsonFilePath);
        var jsonNode = JsonNode.Parse(jsonText);

        if (jsonNode is not JsonObject rootObject)
            throw new InvalidOperationException("JSON file must contain a root object");

        // Get the encrypted envelope from the path
        var envelope = GetEnvelopeAtPath(rootObject, propertyPath);

        // Decrypt the value
        var crypto = new X509HybridCrypto(certificate);
        return crypto.DecryptToString(envelope);
    }

    /// <summary>
    /// Rotates the certificate for an encrypted value by decrypting with old certificate and re-encrypting with new certificate.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON configuration file</param>
    /// <param name="propertyPath">Property path using colon separator (e.g., "Database:ConnectionString")</param>
    /// <param name="oldCertificate">X.509 certificate with private key for decryption</param>
    /// <param name="newCertificate">X.509 certificate with public key for encryption</param>
    /// <param name="newKid">Optional new key identifier (if null, keeps the existing kid)</param>
    /// <exception cref="FileNotFoundException">If file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If JSON file doesn't contain a root object or property path is invalid</exception>
    public static async Task RotateCertificateInFileAsync(
        string jsonFilePath,
        string propertyPath,
        X509Certificate2 oldCertificate,
        X509Certificate2 newCertificate,
        string? newKid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
        ArgumentNullException.ThrowIfNull(oldCertificate);
        ArgumentNullException.ThrowIfNull(newCertificate);

        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");

        // Read JSON file
        var jsonText = await File.ReadAllTextAsync(jsonFilePath);
        var jsonNode = JsonNode.Parse(jsonText);

        if (jsonNode is not JsonObject rootObject)
            throw new InvalidOperationException("JSON file must contain a root object");

        // Get the encrypted envelope and kid from the path
        var (envelope, existingKid) = GetEnvelopeWithKidAtPath(rootObject, propertyPath);

        // Decrypt with old certificate
        var oldCrypto = new X509HybridCrypto(oldCertificate);
        var plaintext = oldCrypto.DecryptToString(envelope);

        // Re-encrypt with new certificate
        var newCrypto = new X509HybridCrypto(newCertificate);
        var newEnvelope = newCrypto.Encrypt(plaintext);

        // Use new kid if provided, otherwise keep existing
        var kidToUse = newKid ?? existingKid;

        // Update the JSON file with the new envelope
        SetValueAtPath(rootObject, propertyPath, newEnvelope, kidToUse);

        // Write back to file
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var updatedJson = JsonSerializer.Serialize(rootObject, options);
        await File.WriteAllTextAsync(jsonFilePath, updatedJson, Encoding.UTF8);
    }

    private static void SetValueAtPath(JsonObject root, string path, HybridSecretEnvelope envelope, string kid)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Property path cannot be empty", nameof(path));

        JsonObject current = root;

        // Navigate to parent, creating objects as needed
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (current[segment] is JsonObject existingObject)
            {
                current = existingObject;
            }
            else
            {
                // Create new object if it doesn't exist
                var newObject = new JsonObject();
                current[segment] = newObject;
                current = newObject;
            }
        }

        // Wrap the envelope in the Cocoar secret structure
        var wrappedEnvelope = new JsonObject
        {
            ["type"] = "cocoar.secret",
            ["version"] = 1,
            ["kid"] = kid,
            ["alg"] = "RSA-OAEP-AES256-GCM"
        };

        // Merge the envelope properties into the wrapped structure
        var envelopeNode = JsonSerializer.SerializeToNode(envelope);
        if (envelopeNode is JsonObject envelopeObject)
        {
            foreach (var prop in envelopeObject)
            {
                wrappedEnvelope[prop.Key] = prop.Value?.DeepClone();
            }
        }

        // Set the final property to the wrapped envelope
        var finalSegment = segments[^1];
        current[finalSegment] = wrappedEnvelope;
    }

    private static HybridSecretEnvelope GetEnvelopeAtPath(JsonObject root, string path)
    {
        var (envelope, _) = GetEnvelopeWithKidAtPath(root, path);
        return envelope;
    }

    private static (HybridSecretEnvelope Envelope, string Kid) GetEnvelopeWithKidAtPath(JsonObject root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Property path cannot be empty", nameof(path));

        JsonNode? current = root;

        // Navigate to the property
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj)
                throw new InvalidOperationException($"Path segment '{segment}' does not exist or is not an object");

            current = obj[segment];
            if (current == null)
                throw new InvalidOperationException($"Property '{segment}' not found in path '{path}'");
        }

        // Expect a typed secret envelope
        if (current is not JsonObject envelopeObj)
            throw new InvalidOperationException($"Value at '{path}' is not an encrypted envelope object");

        var type = envelopeObj["type"]?.GetValue<string>();
        var version = envelopeObj["version"]?.GetValue<int?>();

        if (!string.Equals(type, "cocoar.secret", StringComparison.OrdinalIgnoreCase) || version is null or not 1)
            throw new InvalidOperationException($"Invalid or missing Cocoar secret envelope at '{path}'");

        var kid = envelopeObj["kid"]?.GetValue<string>() ?? "default";

        // Deserialize the envelope
        var envelope = JsonSerializer.Deserialize<HybridSecretEnvelope>(envelopeObj.ToJsonString())
            ?? throw new InvalidOperationException($"Failed to deserialize envelope at '{path}'");

        return (envelope, kid);
    }

    private static JsonNode? GetValueAtPath(JsonObject root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Property path cannot be empty", nameof(path));

        JsonNode? current = root;

        // Navigate to the property
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj)
                throw new InvalidOperationException($"Path segment '{segment}' does not exist or is not an object");

            current = obj[segment];
            if (current == null)
                throw new InvalidOperationException($"Property '{segment}' not found in path '{path}'");
        }

        return current;
    }
}
