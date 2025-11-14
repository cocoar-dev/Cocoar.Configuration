using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class DecryptCommand
{
    public static Command Create()
    {
        var command = new Command("decrypt", "Decrypt a value from a JSON file and optionally re-encrypt with a new certificate");

        var fileOption = new Option<FileInfo>(
            aliases: ["--file", "-f"],
            description: "Path to the JSON configuration file")
        {
            IsRequired = true
        };

        var pathOption = new Option<string>(
            aliases: ["--path", "-p"],
            description: "Property path of the encrypted value (e.g. 'Database:ConnectionString')")
        {
            IsRequired = true
        };

        var oldCertOption = new Option<FileInfo>(
            aliases: ["--old-cert"],
            description: "Path to the PFX certificate file used for current encryption")
        {
            IsRequired = true
        };

        var oldPasswordOption = new Option<string?>(
            aliases: ["--old-password"],
            description: "Password for the old certificate (will prompt if not provided)");

        var newCertOption = new Option<FileInfo?>(
            aliases: ["--new-cert"],
            description: "Path to the new PFX certificate file for re-encryption (optional, for certificate rotation)");

        var newPasswordOption = new Option<string?>(
            aliases: ["--new-password"],
            description: "Password for the new certificate (will prompt if not provided)");

        var showValueOption = new Option<bool>(
            aliases: ["--show"],
            description: "Show the decrypted plaintext value (WARNING: exposes secret in terminal)",
            getDefaultValue: () => false);

        command.AddOption(fileOption);
        command.AddOption(pathOption);
        command.AddOption(oldCertOption);
        command.AddOption(oldPasswordOption);
        command.AddOption(newCertOption);
        command.AddOption(newPasswordOption);
        command.AddOption(showValueOption);

        command.SetHandler(async (file, path, oldCert, oldPassword, newCert, newPassword, showValue) =>
        {
            try
            {
                await DecryptAndRotateAsync(file, path, oldCert, oldPassword, newCert, newPassword, showValue);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileOption, pathOption, oldCertOption, oldPasswordOption, newCertOption, newPasswordOption, showValueOption);

        return command;
    }

    private static async Task DecryptAndRotateAsync(
        FileInfo jsonFile,
        string propertyPath,
        FileInfo oldCertFile,
        string? oldPassword,
        FileInfo? newCertFile,
        string? newPassword,
        bool showValue)
    {
        // Validate inputs
        if (!jsonFile.Exists)
            throw new FileNotFoundException($"JSON file not found: {jsonFile.FullName}");

        if (!oldCertFile.Exists)
            throw new FileNotFoundException($"Old certificate file not found: {oldCertFile.FullName}");

        if (newCertFile != null && !newCertFile.Exists)
            throw new FileNotFoundException($"New certificate file not found: {newCertFile.FullName}");

        // Prompt for old certificate password if not provided
        if (string.IsNullOrEmpty(oldPassword))
        {
            Console.Write("Enter old certificate password: ");
            oldPassword = ReadPassword();
            Console.WriteLine();
        }

        // Load old certificate and decrypt
        var oldCertificate = X509HybridCrypto.LoadCertificate(oldCertFile.FullName, oldPassword);
        var oldCrypto = new X509HybridCrypto(oldCertificate);

        // Read JSON file
        var jsonText = await File.ReadAllTextAsync(jsonFile.FullName);
        var jsonNode = JsonNode.Parse(jsonText);

        if (jsonNode is not JsonObject rootObject)
            throw new InvalidOperationException("JSON file must contain a root object");

        // Get the encrypted envelope from the path
        var envelope = GetEnvelopeAtPath(rootObject, propertyPath);

        // Decrypt the value
        var plaintext = oldCrypto.DecryptToString(envelope);

        Console.WriteLine($"✓ Successfully decrypted value at '{propertyPath}'");

        if (showValue)
        {
            Console.WriteLine($"\n⚠️  WARNING: Plaintext secret exposed in terminal!");
            Console.WriteLine($"Decrypted value: {plaintext}\n");
        }

        // If new certificate provided, re-encrypt and update file
        if (newCertFile != null)
        {
            Console.WriteLine("Re-encrypting with new certificate...");

            // Prompt for new certificate password if not provided
            if (string.IsNullOrEmpty(newPassword))
            {
                Console.Write("Enter new certificate password: ");
                newPassword = ReadPassword();
                Console.WriteLine();
            }

            // Load new certificate and re-encrypt
            var newCertificate = X509HybridCrypto.LoadCertificate(newCertFile.FullName, newPassword);
            var newCrypto = new X509HybridCrypto(newCertificate);

            var newEnvelope = newCrypto.Encrypt(plaintext);

            // Update the JSON file with the new envelope
            SetEnvelopeAtPath(rootObject, propertyPath, newEnvelope);

            // Write back to file with nice formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var updatedJson = JsonSerializer.Serialize(rootObject, options);
            await File.WriteAllTextAsync(jsonFile.FullName, updatedJson, Encoding.UTF8);

            Console.WriteLine($"✓ Successfully re-encrypted value with new certificate");
            Console.WriteLine($"  New wrapping algorithm: {newEnvelope.WrappingAlgorithm}");
        }
        else
        {
            Console.WriteLine("\nNo new certificate provided. Value was decrypted but not re-encrypted.");
            Console.WriteLine("To rotate certificates, use --new-cert option.");
        }
    }

    private static HybridSecretEnvelope GetEnvelopeAtPath(JsonObject root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Property path cannot be empty", nameof(path));

        JsonNode? current = root;

        // Navigate to the property
        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                throw new InvalidOperationException($"Property path '{path}' not found in JSON");
            }
        }

        if (current == null)
            throw new InvalidOperationException($"Property at '{path}' is null");

        // Deserialize the envelope
        try
        {
            var envelope = JsonSerializer.Deserialize<HybridSecretEnvelope>(current.ToJsonString());
            if (envelope == null)
                throw new InvalidOperationException("Failed to deserialize envelope");

            return envelope;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Property at '{path}' is not a valid HybridSecretEnvelope", ex);
        }
    }

    private static void SetEnvelopeAtPath(JsonObject root, string path, HybridSecretEnvelope envelope)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Property path cannot be empty", nameof(path));

        JsonObject current = root;

        // Navigate to parent
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (current[segment] is JsonObject existingObject)
            {
                current = existingObject;
            }
            else
            {
                throw new InvalidOperationException($"Cannot navigate to '{path}': parent object not found");
            }
        }

        // Set the final property to the new envelope
        var finalSegment = segments[^1];
        var envelopeJson = JsonSerializer.SerializeToNode(envelope);
        current[finalSegment] = envelopeJson;
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        } while (key.Key != ConsoleKey.Enter);

        return password.ToString();
    }
}
