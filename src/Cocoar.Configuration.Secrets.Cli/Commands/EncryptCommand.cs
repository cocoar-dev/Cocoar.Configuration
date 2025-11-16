using System.CommandLine;
using System.Text;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class EncryptCommand
{
    public static Command Create()
    {
        var command = new Command("encrypt", "Encrypt a value and set it at a property path in a JSON file");

        var fileOption = new Option<FileInfo>("--file")
        {
            Description = "Path to the JSON configuration file",
            Required = true
        };
        fileOption.Aliases.Add("-f");

        var pathOption = new Option<string>("--path")
        {
            Description = "Property path where to set the encrypted value (e.g. 'Database:ConnectionString' or 'ApiKeys:Stripe')",
            Required = true
        };
        pathOption.Aliases.Add("-p");

        var valueOption = new Option<string?>("--value")
        {
            Description = "The plaintext value to encrypt. If omitted, encrypts the existing value at the specified path.",
            Required = false
        };
        valueOption.Aliases.Add("-v");

        var certOption = new Option<FileInfo>("--cert")
        {
            Description = "Path to the PFX certificate file for encryption",
            Required = true
        };
        certOption.Aliases.Add("-c");

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Password for the PFX certificate (will prompt if not provided)"
        };
        passwordOption.Aliases.Add("-pwd");

        var kidOption = new Option<string>("--kid")
        {
            Description = "Key identifier (kid) for the certificate",
            DefaultValueFactory = _ => "default"
        };

        var createOption = new Option<bool>("--create")
        {
            Description = "Create the JSON file if it doesn't exist (prevents accidental file creation from typos)",
            DefaultValueFactory = _ => false
        };

        command.Options.Add(fileOption);
        command.Options.Add(pathOption);
        command.Options.Add(valueOption);
        command.Options.Add(certOption);
        command.Options.Add(passwordOption);
        command.Options.Add(kidOption);
        command.Options.Add(createOption);

        command.SetAction(parseResult =>
        {
            try
            {
                var file = parseResult.GetValue(fileOption);
                var path = parseResult.GetValue(pathOption);
                var value = parseResult.GetValue(valueOption);
                var cert = parseResult.GetValue(certOption);
                var password = parseResult.GetValue(passwordOption);
                var kid = parseResult.GetValue(kidOption);
                var create = parseResult.GetValue(createOption);
                // fileOption, pathOption, certOption have Required = true; kidOption has DefaultValueFactory
                EncryptValueAsync(file!, path!, value, cert!, password, kid!, create).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static async Task EncryptValueAsync(
        FileInfo jsonFile,
        string propertyPath,
        string? plaintext,
        FileInfo certFile,
        string? password,
        string kid,
        bool allowCreate)
    {
        // Validate JSON file exists or creation is allowed
        if (!jsonFile.Exists && !allowCreate)
            throw new FileNotFoundException($"JSON file not found: {jsonFile.FullName}. Use --create to create a new file.");

        // Validate certificate file exists
        if (!certFile.Exists)
            throw new FileNotFoundException($"Certificate file not found: {certFile.FullName}");

        // Prompt for password if not provided
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Enter certificate password: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        // Load certificate
        var certificate = X509HybridCrypto.LoadCertificate(certFile.FullName, password);

        bool wasCreated;
        
        if (plaintext is not null)
        {
            // Encrypt the provided value
            // Try to parse as JSON first (handles numbers, booleans, null, objects, arrays)
            // If parsing fails, treat as a string and serialize it
            string jsonValue;
            try
            {
                // Attempt to parse as JSON - this validates JSON syntax
                using var doc = System.Text.Json.JsonDocument.Parse(plaintext);
                // If successful, use the original value as-is (it's valid JSON)
                jsonValue = plaintext;
            }
            catch (System.Text.Json.JsonException)
            {
                // Not valid JSON, treat as a string and serialize it with quotes
                jsonValue = System.Text.Json.JsonSerializer.Serialize(plaintext);
            }
            
            wasCreated = await JsonSecretsEditor.EncryptValueInFileAsync(
                jsonFile.FullName,
                propertyPath,
                jsonValue,
                certificate,
                kid,
                allowCreate);
        }
        else
        {
            // Encrypt existing value at the path
            wasCreated = await JsonSecretsEditor.EncryptExistingValueInFileAsync(
                jsonFile.FullName,
                propertyPath,
                certificate,
                kid);
        }

        var action = wasCreated ? "created file and encrypted value at" : "encrypted value at";
        Console.WriteLine($"✓ Successfully {action} '{propertyPath}' in {jsonFile.Name}");
        Console.WriteLine($"  Key ID (kid): {kid}");
        Console.WriteLine($"  Wrapping algorithm: RSA-OAEP-256");
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
