using System.CommandLine;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class GenerateCertCommand
{
    public static Command Create()
    {
        var command = new Command("generate-cert", "Generate a self-signed certificate for encryption");

        var outputOption = new Option<string>("--output")
        {
            Description = "Output path for certificate file(s)",
            Required = true
        };
        outputOption.Aliases.Add("-o");

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Password for PFX file (required for --format pfx)"
        };
        passwordOption.Aliases.Add("-pwd");

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: pfx (default), pem, or auto",
            DefaultValueFactory = _ => "pfx"
        };

        var subjectOption = new Option<string>("--subject")
        {
            Description = "Certificate subject",
            DefaultValueFactory = _ => "CN=Cocoar Secrets"
        };
        subjectOption.Aliases.Add("-s");

        var validYearsOption = new Option<int>("--valid-years")
        {
            Description = "Validity period in years",
            DefaultValueFactory = _ => 1
        };

        var keySizeOption = new Option<int>("--key-size")
        {
            Description = "RSA key size (2048, 3072, or 4096)",
            DefaultValueFactory = _ => 2048
        };

        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Overwrite existing file without prompt",
            DefaultValueFactory = _ => false
        };

        command.Options.Add(outputOption);
        command.Options.Add(passwordOption);
        command.Options.Add(formatOption);
        command.Options.Add(subjectOption);
        command.Options.Add(validYearsOption);
        command.Options.Add(keySizeOption);
        command.Options.Add(overwriteOption);

        command.SetAction(parseResult =>
        {
            var output = parseResult.GetValue(outputOption);
            var password = parseResult.GetValue(passwordOption);
            var format = parseResult.GetValue(formatOption);
            var subject = parseResult.GetValue(subjectOption);
            var validYears = parseResult.GetValue(validYearsOption);
            var keySize = parseResult.GetValue(keySizeOption);
            var overwrite = parseResult.GetValue(overwriteOption);
            return ExecuteAsync(output, password, format, subject, validYears, keySize, overwrite).GetAwaiter().GetResult();
        });

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
