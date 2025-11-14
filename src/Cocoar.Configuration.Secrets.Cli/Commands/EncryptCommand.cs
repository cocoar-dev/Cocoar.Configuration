using System.CommandLine;
using System.Text;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class EncryptCommand
{
    public static Command Create()
    {
        var command = new Command("encrypt", "Encrypt a value and set it at a property path in a JSON file");

        var fileOption = new Option<FileInfo>(
            aliases: ["--file", "-f"],
            description: "Path to the JSON configuration file")
        {
            IsRequired = true
        };

        var pathOption = new Option<string>(
            aliases: ["--path", "-p"],
            description: "Property path where to set the encrypted value (e.g. 'Database:ConnectionString' or 'ApiKeys:Stripe')")
        {
            IsRequired = true
        };

        var valueOption = new Option<string>(
            aliases: ["--value", "-v"],
            description: "The plaintext value to encrypt")
        {
            IsRequired = true
        };

        var certOption = new Option<FileInfo>(
            aliases: ["--cert", "-c"],
            description: "Path to the PFX certificate file for encryption")
        {
            IsRequired = true
        };

        var passwordOption = new Option<string?>(
            aliases: ["--password", "-pwd"],
            description: "Password for the PFX certificate (will prompt if not provided)");

        var kidOption = new Option<string>(
            aliases: ["--kid"],
            description: "Key identifier (kid) for the certificate",
            getDefaultValue: () => "default");

        var createOption = new Option<bool>(
            aliases: ["--create"],
            description: "Create the JSON file if it doesn't exist (prevents accidental file creation from typos)",
            getDefaultValue: () => false);

        command.AddOption(fileOption);
        command.AddOption(pathOption);
        command.AddOption(valueOption);
        command.AddOption(certOption);
        command.AddOption(passwordOption);
        command.AddOption(kidOption);
        command.AddOption(createOption);

        command.SetHandler(async (file, path, value, cert, password, kid, create) =>
        {
            try
            {
                await EncryptValueAsync(file, path, value, cert, password, kid, create);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileOption, pathOption, valueOption, certOption, passwordOption, kidOption, createOption);

        return command;
    }

    private static async Task EncryptValueAsync(
        FileInfo jsonFile,
        string propertyPath,
        string plaintext,
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

        // Use the JsonSecretsEditor to encrypt the value in the file
        var wasCreated = await JsonSecretsEditor.EncryptValueInFileAsync(
            jsonFile.FullName,
            propertyPath,
            plaintext,
            certificate,
            kid,
            allowCreate);

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
