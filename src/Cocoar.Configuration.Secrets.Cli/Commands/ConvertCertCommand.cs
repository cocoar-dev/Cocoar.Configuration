using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class ConvertCertCommand
{
    public static Command Create()
    {
        var command = new Command("convert-cert", "Convert certificate between PFX and PEM formats");

        var inputOption = new Option<string>(
            aliases: ["--input", "-i"],
            description: "Input certificate file (.pfx, .p12, .crt, .pem, .cer)")
        {
            IsRequired = true
        };

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "Output certificate file")
        {
            IsRequired = true
        };

        var inputPasswordOption = new Option<string?>(
            aliases: ["--input-password", "--ipass"],
            description: "Password for input PFX file (required when converting from PFX)");

        var outputPasswordOption = new Option<string?>(
            aliases: ["--output-password", "--opass"],
            description: "Password for output PFX file (required when converting to PFX)");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "Output format: pfx, pem, or auto (detect from extension)",
            getDefaultValue: () => "auto");

        var overwriteOption = new Option<bool>(
            aliases: ["--overwrite"],
            description: "Overwrite existing output file(s) without prompt",
            getDefaultValue: () => false);

        command.AddOption(inputOption);
        command.AddOption(outputOption);
        command.AddOption(inputPasswordOption);
        command.AddOption(outputPasswordOption);
        command.AddOption(formatOption);
        command.AddOption(overwriteOption);

        command.SetHandler(async (input, output, inputPassword, outputPassword, format, overwrite) =>
        {
            await ExecuteAsync(input, output, inputPassword, outputPassword, format, overwrite);
        }, inputOption, outputOption, inputPasswordOption, outputPasswordOption, formatOption, overwriteOption);

        return command;
    }

    private static Task<int> ExecuteAsync(
        string input,
        string output,
        string? inputPassword,
        string? outputPassword,
        string format,
        bool overwrite)
    {
        try
        {
            // Detect formats
            var inputFormat = DetectFormat(input);
            var outputFormat = format.ToLowerInvariant() switch
            {
                "pfx" => CertificateFormat.Pfx,
                "pem" => CertificateFormat.Pem,
                "auto" => DetectFormat(output),
                _ => throw new ArgumentException($"Invalid format '{format}'. Use 'pfx', 'pem', or 'auto'.")
            };

            // Validate passwords
            if (inputFormat == CertificateFormat.Pfx && string.IsNullOrWhiteSpace(inputPassword))
            {
                Console.Error.WriteLine("❌ Error: --input-password is required when converting from PFX");
                return Task.FromResult(1);
            }

            if (outputFormat == CertificateFormat.Pfx && string.IsNullOrWhiteSpace(outputPassword))
            {
                Console.Error.WriteLine("❌ Error: --output-password is required when converting to PFX");
                return Task.FromResult(1);
            }

            // Check for overwrite
            if (!overwrite)
            {
                if (outputFormat == CertificateFormat.Pfx && File.Exists(output))
                {
                    Console.Error.WriteLine($"❌ Error: Output file already exists: {output}. Use --overwrite to replace.");
                    return Task.FromResult(2);
                }
                else if (outputFormat == CertificateFormat.Pem)
                {
                    var keyPath = Path.ChangeExtension(output, ".key");
                    if (File.Exists(output) || File.Exists(keyPath))
                    {
                        Console.Error.WriteLine($"❌ Error: Output files already exist: {output} or {keyPath}. Use --overwrite to replace.");
                        return Task.FromResult(2);
                    }
                }
            }

            // Load input certificate
            X509Certificate2 cert;
            if (inputFormat == CertificateFormat.Pfx)
            {
                Console.WriteLine($"Loading PFX: {input}");
                var keyPath = outputFormat == CertificateFormat.Pem ? Path.ChangeExtension(output, ".key") : null;
                cert = outputFormat == CertificateFormat.Pem
                    ? X509CertificateGenerator.ConvertPfxToPem(input, inputPassword!, output, keyPath, overwrite)
                    : throw new InvalidOperationException("Cannot convert PFX to PFX. Use same format.");
            }
            else
            {
                Console.WriteLine($"Loading PEM: {input} + {Path.ChangeExtension(input, ".key")}");
                cert = outputFormat == CertificateFormat.Pfx
                    ? X509CertificateGenerator.ConvertPemToPfx(input, null, output, outputPassword!, overwrite)
                    : throw new InvalidOperationException("Cannot convert PEM to PEM. Use same format.");
            }

            // Display success
            try
            {
                if (outputFormat == CertificateFormat.Pfx)
                {
                    Console.WriteLine($"✓ Certificate converted to PFX: {output}");
                }
                else
                {
                    var keyPath = Path.ChangeExtension(output, ".key");
                    Console.WriteLine($"✓ Certificate converted to PEM:");
                    Console.WriteLine($"  Certificate: {output}");
                    Console.WriteLine($"  Private Key: {keyPath}");
                }

                Console.WriteLine($"  Subject: {cert.Subject}");
                Console.WriteLine($"  Valid: {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");
                Console.WriteLine($"  Thumbprint: {cert.Thumbprint}");

                return Task.FromResult(0);
            }
            finally
            {
                cert.Dispose();
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(1);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(2);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return Task.FromResult(2);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Console.Error.WriteLine($"❌ Error: Failed to load certificate. Check password and file format.");
            Console.Error.WriteLine($"   Details: {ex.Message}");
            return Task.FromResult(3);
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
            _ => CertificateFormat.Pfx // Default
        };
    }
}
