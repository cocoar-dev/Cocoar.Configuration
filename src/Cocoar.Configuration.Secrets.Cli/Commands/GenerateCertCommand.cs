using System.CommandLine;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class GenerateCertCommand
{
    public static Command Create()
    {
        var command = new Command("generate-cert", "Generate a self-signed certificate for encryption");

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "Output path for certificate file(s)")
        {
            IsRequired = true
        };

        var passwordOption = new Option<string?>(
            aliases: ["--password", "-pwd"],
            description: "Password for PFX file (required for --format pfx)");

        var formatOption = new Option<string>(
            aliases: ["--format"],
            description: "Output format: pfx (default), pem, or auto",
            getDefaultValue: () => "pfx");

        var subjectOption = new Option<string>(
            aliases: ["--subject", "-s"],
            description: "Certificate subject",
            getDefaultValue: () => "CN=Cocoar Secrets");

        var validYearsOption = new Option<int>(
            aliases: ["--valid-years"],
            description: "Validity period in years",
            getDefaultValue: () => 1);

        var keySizeOption = new Option<int>(
            aliases: ["--key-size"],
            description: "RSA key size (2048, 3072, or 4096)",
            getDefaultValue: () => 2048);

        var overwriteOption = new Option<bool>(
            aliases: ["--overwrite"],
            description: "Overwrite existing file without prompt",
            getDefaultValue: () => false);

        command.AddOption(outputOption);
        command.AddOption(passwordOption);
        command.AddOption(formatOption);
        command.AddOption(subjectOption);
        command.AddOption(validYearsOption);
        command.AddOption(keySizeOption);
        command.AddOption(overwriteOption);

        command.SetHandler(async (output, password, format, subject, validYears, keySize, overwrite) =>
        {
            await ExecuteAsync(output, password, format, subject, validYears, keySize, overwrite);
        }, outputOption, passwordOption, formatOption, subjectOption, validYearsOption, keySizeOption, overwriteOption);

        return command;
    }

    private static Task<int> ExecuteAsync(
        string output,
        string? password,
        string format,
        string subject,
        int validYears,
        int keySize,
        bool overwrite)
    {
        try
        {
            // Determine format
            var certFormat = format.ToLowerInvariant() switch
            {
                "pfx" => CertificateFormat.Pfx,
                "pem" => CertificateFormat.Pem,
                "auto" => DetectFormat(output),
                _ => throw new ArgumentException($"Invalid format '{format}'. Use 'pfx', 'pem', or 'auto'.")
            };

            // Validate password for PFX
            if (certFormat == CertificateFormat.Pfx && string.IsNullOrWhiteSpace(password))
            {
                Console.Error.WriteLine("❌ Error: --password is required for PFX format");
                return Task.FromResult(1);
            }

            // Generate certificate
            using var cert = certFormat == CertificateFormat.Pfx
                ? X509CertificateGenerator.GenerateAndSavePfx(output, password!, subject, validYears, keySize, overwrite)
                : X509CertificateGenerator.GenerateAndSavePem(output, null, subject, validYears, keySize, overwrite);

            // Display success
            if (certFormat == CertificateFormat.Pfx)
            {
                Console.WriteLine($"✓ Certificate generated (PFX): {output}");
            }
            else
            {
                var keyPath = Path.ChangeExtension(output, ".key");
                Console.WriteLine($"✓ Certificate generated (PEM):");
                Console.WriteLine($"  Certificate: {output}");
                Console.WriteLine($"  Private Key: {keyPath}");
            }

            Console.WriteLine($"  Subject: {cert.Subject}");
            Console.WriteLine($"  Valid: {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");
            Console.WriteLine($"  Key Size: {keySize} bits");
            Console.WriteLine($"  Thumbprint: {cert.Thumbprint}");

            return Task.FromResult(0);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(1);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(2);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(4);
        }
    }

    private static CertificateFormat DetectFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pfx" or ".p12" => CertificateFormat.Pfx,
            ".pem" or ".crt" or ".cer" => CertificateFormat.Pem,
            _ => CertificateFormat.Pfx // Default to PFX
        };
    }
}
