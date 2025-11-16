using System.CommandLine;
using Cocoar.Configuration.Secrets.Cli.Commands;

namespace Cocoar.Configuration.Secrets.Cli;

internal class Program
{
    static int Main(string[] args)
    {
        var rootCommand = new RootCommand("Cocoar.Configuration.Secrets CLI - Encrypt secrets in JSON configuration files")
        {
            GenerateCertCommand.Create(),
            ConvertCertCommand.Create(),
            EncryptCommand.Create(),
            DecryptCommand.Create(),
            CertInfoCommand.Create()
        };

        return rootCommand.Parse(args).Invoke();
    }
}
