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
        var command = new Command("decrypt", "Decrypt an encrypted value from a JSON file");

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

        var certOption = new Option<FileInfo>(
            aliases: ["--cert", "-c"],
            description: "Path to the PFX certificate file for decryption")
        {
            IsRequired = true
        };

        var passwordOption = new Option<string?>(
            aliases: ["--password", "-pwd"],
            description: "Password for the certificate (will prompt if not provided)");

        var replaceOption = new Option<bool>(
            aliases: ["--replace"],
            description: "Replace the encrypted value with plaintext in the JSON file (WARNING: modifies file)",
            getDefaultValue: () => false);

        command.AddOption(fileOption);
        command.AddOption(pathOption);
        command.AddOption(certOption);
        command.AddOption(passwordOption);
        command.AddOption(replaceOption);

        command.SetHandler(async (file, path, cert, password, replace) =>
        {
            try
            {
                await DecryptAsync(file, path, cert, password, replace);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileOption, pathOption, certOption, passwordOption, replaceOption);

        return command;
    }

    private static async Task DecryptAsync(
        FileInfo jsonFile,
        string propertyPath,
        FileInfo certFile,
        string? password,
        bool replace)
    {
        // Validate inputs
        if (!jsonFile.Exists)
            throw new FileNotFoundException($"JSON file not found: {jsonFile.FullName}");

        if (!certFile.Exists)
            throw new FileNotFoundException($"Certificate file not found: {certFile.FullName}");

        // Prompt for certificate password if not provided
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Enter certificate password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        // Load certificate and decrypt
        var certificate = X509HybridCrypto.LoadCertificate(certFile.FullName, password);
        var crypto = new X509HybridCrypto(certificate);

        // Read JSON file
        var jsonText = await File.ReadAllTextAsync(jsonFile.FullName);
        var jsonNode = JsonNode.Parse(jsonText);

        if (jsonNode is not JsonObject rootObject)
            throw new InvalidOperationException("JSON file must contain a root object");

        // Get the encrypted envelope from the path
        var envelope = GetEnvelopeAtPath(rootObject, propertyPath);

        // Decrypt the value
        var plaintext = crypto.DecryptToString(envelope);

        if (replace)
        {
            // Replace encrypted value with plaintext in JSON file
            SetPlaintextAtPath(rootObject, propertyPath, plaintext);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var updatedJson = JsonSerializer.Serialize(rootObject, options);
            await File.WriteAllTextAsync(jsonFile.FullName, updatedJson, Encoding.UTF8);

            Console.WriteLine($"✓ Successfully decrypted value at '{propertyPath}' and replaced in file");
        }
        else
        {
            // Default: just show the decrypted value, don't modify file
            Console.WriteLine($"✓ Successfully decrypted value at '{propertyPath}'");
            Console.WriteLine($"\nDecrypted value:\n{plaintext}");
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

    private static void SetPlaintextAtPath(JsonObject root, string path, string plaintext)
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

        // Set the final property to the plaintext JSON value
        var finalSegment = segments[^1];
        
        // Parse plaintext as JSON to preserve type (string, number, boolean, etc.)
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            current[finalSegment] = JsonNode.Parse(plaintext);
        }
        catch (JsonException)
        {
            // If not valid JSON, treat as string literal
            current[finalSegment] = JsonValue.Create(plaintext);
        }
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
