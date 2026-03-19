using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class DecryptCommand
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Command Create()
    {
        var command = new Command("decrypt", "Decrypt an encrypted value from a JSON file");

        var fileOption = new Option<FileInfo>("--file")
        {
            Description = "Path to the JSON configuration file",
            Required = true
        };
        fileOption.Aliases.Add("-f");

        var pathOption = new Option<string>("--path")
        {
            Description = "Property path of the encrypted value (e.g. 'Database:ConnectionString')",
            Required = true
        };
        pathOption.Aliases.Add("-p");

        var certOption = new Option<FileInfo>("--cert")
        {
            Description = "Path to the PFX certificate file for decryption",
            Required = true
        };
        certOption.Aliases.Add("-c");

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Password for the certificate (will prompt if not provided)"
        };
        passwordOption.Aliases.Add("-pwd");

        var replaceOption = new Option<bool>("--replace")
        {
            Description = "Replace the encrypted value with plaintext in the JSON file (WARNING: modifies file)",
            DefaultValueFactory = _ => false
        };

        command.Options.Add(fileOption);
        command.Options.Add(pathOption);
        command.Options.Add(certOption);
        command.Options.Add(passwordOption);
        command.Options.Add(replaceOption);

        command.SetAction(parseResult =>
        {
            try
            {
                var file = parseResult.GetValue(fileOption);
                var path = parseResult.GetValue(pathOption);
                var cert = parseResult.GetValue(certOption);
                var password = parseResult.GetValue(passwordOption);
                var replace = parseResult.GetValue(replaceOption);
                // fileOption, pathOption, certOption have Required = true
                DecryptAsync(file!, path!, cert!, password, replace).GetAwaiter().GetResult();
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
                return 2;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
                return 2;
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                Console.Error.WriteLine($"❌ Error: Decryption failed. Check certificate and encrypted value.");
                Console.Error.WriteLine($"   Details: {ex.Message}");
                return 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Error: {ex.Message}");
                return 4;
            }
        });

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

            var updatedJson = JsonSerializer.Serialize(rootObject, IndentedJsonOptions);
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

        var result = password.ToString();

        // Zero the StringBuilder buffer so the password doesn't linger in heap memory
        for (var i = 0; i < password.Length; i++)
            password[i] = '\0';
        password.Clear();

        return result;
    }
}
