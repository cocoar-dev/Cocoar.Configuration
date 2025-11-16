using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class ConvertCertCommand
{
    public static Command Create()
    {
        var command = new Command("convert-cert", "Convert certificate between PFX and PEM formats");

        var inputOption = new Option<string>("--input")
        {
            Description = "Input certificate file (.pfx, .p12, .crt, .pem, .cer)",
            Required = true
        };
        inputOption.Aliases.Add("-i");

        var outputOption = new Option<string>("--output")
        {
            Description = "Output certificate file",
            Required = true
        };
        outputOption.Aliases.Add("-o");

        var inputPasswordOption = new Option<string?>("--input-password")
        {
            Description = "Password for input PFX file (required when converting from PFX)"
        };
        inputPasswordOption.Aliases.Add("--ipass");

        var outputPasswordOption = new Option<string?>("--output-password")
        {
            Description = "Password for output PFX file (optional; omit for password-less PFX)"
        };
        outputPasswordOption.Aliases.Add("--opass");

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: pfx, pem, or auto (detect from extension)",
            DefaultValueFactory = _ => "auto"
        };
        formatOption.Aliases.Add("-f");

        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Overwrite existing output file(s) without prompt",
            DefaultValueFactory = _ => false
        };

        command.Options.Add(inputOption);
        command.Options.Add(outputOption);
        command.Options.Add(inputPasswordOption);
        command.Options.Add(outputPasswordOption);
        command.Options.Add(formatOption);
        command.Options.Add(overwriteOption);

        command.SetAction(parseResult =>
        {
            var input = parseResult.GetValue(inputOption);
            var output = parseResult.GetValue(outputOption);
            var inputPassword = parseResult.GetValue(inputPasswordOption);
            var outputPassword = parseResult.GetValue(outputPasswordOption);
            var format = parseResult.GetValue(formatOption);
            var overwrite = parseResult.GetValue(overwriteOption);
            // inputOption and outputOption have Required = true; formatOption has DefaultValueFactory
            return ExecuteAsync(input!, output!, inputPassword, outputPassword, format!, overwrite).GetAwaiter().GetResult();
        });

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
            var inputFormat = DetectFormat(input);
            var outputFormat = format.ToLowerInvariant() switch
            {
                "pfx" => CertificateFormat.Pfx,
                "pem" => CertificateFormat.Pem,
                "auto" => DetectFormat(output),
                _ => throw new ArgumentException($"Invalid format '{format}'. Use 'pfx', 'pem', or 'auto'.")
            };

            // Input password optional - will try loading without password if not provided
            // Output password optional - password-less by default
            var useOutputPassword = !string.IsNullOrWhiteSpace(outputPassword);

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

            X509Certificate2 cert;
            if (inputFormat == CertificateFormat.Pfx)
            {
                Console.WriteLine($"Loading PFX: {input}");
                if (outputFormat == CertificateFormat.Pem)
                {
                    var keyPath = Path.ChangeExtension(output, ".key");
                    cert = X509CertificateGenerator.ConvertPfxToPem(input, inputPassword!, output, keyPath, overwrite);
                }
                else
                {
                    // PFX → PFX: password change or removal
                    cert = X509CertificateLoader.LoadPkcs12FromFile(input, inputPassword, X509KeyStorageFlags.Exportable);
                    var exportBytes = cert.Export(X509ContentType.Pfx, outputPassword);
                    File.WriteAllBytes(output, exportBytes);
                }
            }
            else
            {
                Console.WriteLine($"Loading PEM: {input} + {Path.ChangeExtension(input, ".key")}");
                cert = outputFormat == CertificateFormat.Pfx
                    ? X509CertificateGenerator.ConvertPemToPfx(input, null, output, outputPassword!, overwrite)
                    : throw new InvalidOperationException("Cannot convert PEM to PEM. Use same format.");
            }

            try
            {
                if (outputFormat == CertificateFormat.Pfx)
                {
                    var passwordStatus = useOutputPassword ? "password-protected" : "password-less";
                    Console.WriteLine($"✓ Certificate converted to PFX ({passwordStatus}): {output}");
                    if (!useOutputPassword)
                    {
                        Console.WriteLine("  ⚠️  Protect with file permissions!");
                        if (OperatingSystem.IsWindows())
                            Console.WriteLine("     Windows: icacls cert.pfx /inheritance:r /grant:r \"YourUser:(R)\"");
                        else
                            Console.WriteLine("     Linux/macOS: chmod 600 cert.pfx && chown app-user cert.pfx");
                    }
                }
                else
                {
                    var keyPath = Path.ChangeExtension(output, ".key");
                    Console.WriteLine($"✓ Certificate converted to PEM:");
                    Console.WriteLine($"  Certificate: {output}");
                    Console.WriteLine($"  Private Key: {keyPath}");
                    Console.WriteLine("  ⚠️  Protect private key with file permissions!");
                    if (OperatingSystem.IsWindows())
                        Console.WriteLine("     Windows: icacls {keyPath} /inheritance:r /grant:r \"YourUser:(R)\"");
                    else
                        Console.WriteLine("     Linux/macOS: chmod 600 {keyPath} && chown app-user {keyPath}");
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
