using System.CommandLine;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class CertInfoCommand
{
    public static Command Create()
    {
        var command = new Command("cert-info", "Display detailed information about a certificate");

        var inputOption = new Option<string>("--input")
        {
            Description = "Certificate file path (PFX or PEM)",
            Required = true
        };
        inputOption.Aliases.Add("-i");

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Certificate password (if password-protected)",
            Required = false
        };
        passwordOption.Aliases.Add("-pwd");

        command.Options.Add(inputOption);
        command.Options.Add(passwordOption);

        command.SetAction(parseResult =>
        {
            var input = parseResult.GetValue(inputOption);
            var password = parseResult.GetValue(passwordOption);
            return ExecuteAsync(input!, password).GetAwaiter().GetResult();
        });

        return command;
    }

    private static Task<int> ExecuteAsync(string input, string? password)
    {
        try
        {
            if (!File.Exists(input))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Certificate file not found: {input}");
                Console.ResetColor();
                return Task.FromResult(1);
            }

            Console.WriteLine($"Analyzing certificate: {input}");
            Console.WriteLine();

            // Try with password first (if provided), then without
            X509Certificate2? cert = null;
            bool isPasswordProtected = false;

            if (!string.IsNullOrEmpty(password))
            {
                try
                {
                    cert = X509CertificateLoader.LoadPkcs12FromFile(input, password, X509KeyStorageFlags.Exportable);
                    isPasswordProtected = true;
                }
                catch
                {
                    cert = X509CertificateLoader.LoadPkcs12FromFile(input, null, X509KeyStorageFlags.Exportable);
                }
            }
            else
            {
                try
                {
                    cert = X509CertificateLoader.LoadPkcs12FromFile(input, null, X509KeyStorageFlags.Exportable);
                }
                catch (CryptographicException ex)
                {
                    if (ex.Message.Contains("password") || ex.HResult == unchecked((int)0x80070056)) // ERROR_INVALID_PASSWORD
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("⚠️  Certificate appears to be password-protected.");
                        Console.WriteLine("   Use --password option to provide the password.");
                        Console.ResetColor();
                        return Task.FromResult(1);
                    }
                    throw;
                }
            }

            using (cert)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("📜 Certificate Information");
                Console.ResetColor();
                Console.WriteLine($"   Subject:       {cert.Subject}");
                Console.WriteLine($"   Issuer:        {cert.Issuer}");
                Console.WriteLine($"   Serial Number: {cert.SerialNumber}");
                Console.WriteLine($"   Thumbprint:    {cert.Thumbprint}");
                Console.WriteLine();

                // Validity
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("📅 Validity");
                Console.ResetColor();
                Console.WriteLine($"   Not Before:    {cert.NotBefore:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"   Not After:     {cert.NotAfter:yyyy-MM-dd HH:mm:ss}");
                
                var now = DateTime.Now;
                if (now < cert.NotBefore)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   Status:        ⚠️  Not yet valid (starts in {(cert.NotBefore - now).Days} days)");
                    Console.ResetColor();
                }
                else if (now > cert.NotAfter)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   Status:        ✗ Expired ({(now - cert.NotAfter).Days} days ago)");
                    Console.ResetColor();
                }
                else
                {
                    var daysUntilExpiry = (cert.NotAfter - now).Days;
                    Console.ForegroundColor = daysUntilExpiry < 30 ? ConsoleColor.Yellow : ConsoleColor.Green;
                    Console.WriteLine($"   Status:        ✓ Valid ({daysUntilExpiry} days remaining)");
                    Console.ResetColor();
                }
                Console.WriteLine();

                // Key Information
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("🔑 Key Information");
                Console.ResetColor();
                
                var rsa = cert.GetRSAPublicKey();
                if (rsa is not null)
                {
                    Console.WriteLine($"   Algorithm:     RSA");
                    Console.WriteLine($"   Key Size:      {rsa.KeySize} bits");
                }
                else
                {
                    var ecdsa = cert.GetECDsaPublicKey();
                    if (ecdsa is not null)
                    {
                        Console.WriteLine($"   Algorithm:     ECDSA");
                        Console.WriteLine($"   Key Size:      {ecdsa.KeySize} bits");
                    }
                    else
                    {
                        Console.WriteLine($"   Algorithm:     {cert.PublicKey.Oid.FriendlyName}");
                    }
                }

                Console.WriteLine($"   Has Private Key: {(cert.HasPrivateKey ? "✓ Yes" : "✗ No")}");
                
                if (cert.HasPrivateKey)
                {
                    Console.ForegroundColor = isPasswordProtected ? ConsoleColor.Yellow : ConsoleColor.Green;
                    Console.WriteLine($"   Password:      {(isPasswordProtected ? "🔒 Protected" : "🔓 None (password-less)")}");
                    Console.ResetColor();
                }
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("🎯 Key Usage");
                Console.ResetColor();
                
                var keyUsageExt = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
                if (keyUsageExt != null)
                {
                    Console.WriteLine($"   {keyUsageExt.KeyUsages}");
                }
                else
                {
                    Console.WriteLine($"   (No key usage extension)");
                }
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("📁 File Information");
                Console.ResetColor();
                var fileInfo = new FileInfo(input);
                Console.WriteLine($"   File Size:     {fileInfo.Length:N0} bytes");
                Console.WriteLine($"   Format:        {(input.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || input.EndsWith(".p12", StringComparison.OrdinalIgnoreCase) ? "PFX (PKCS#12)" : "PEM")}");
                Console.WriteLine($"   Created:       {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"   Modified:      {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();

                if (isPasswordProtected)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("💡 Recommendation:");
                    Console.WriteLine("   Consider converting to password-less format for production use:");
                    Console.WriteLine($"   cocoar-secrets remove-password -i \"{input}\" -pwd \"YourPassword\" -o passwordless.pfx");
                    Console.ResetColor();
                }
                else if (!cert.HasPrivateKey)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚠️  Warning:");
                    Console.WriteLine("   This certificate has no private key - cannot be used for decryption.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Certificate is ready for use (password-less with private key)");
                    Console.ResetColor();
                }
            }

            return Task.FromResult(0);
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(1);
        }
        catch (FileNotFoundException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(2);
        }
        catch (IOException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(2);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error: Failed to read certificate. Check password and file format.");
            Console.WriteLine($"  Details: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(3);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.ResetColor();
            return Task.FromResult(4);
        }
    }
}
