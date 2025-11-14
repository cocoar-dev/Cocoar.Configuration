using System.CommandLine;

namespace Cocoar.Configuration.Secrets.Cli.Commands;

internal static class CertInfoCommand
{
    public static Command Create()
    {
        var command = new Command("cert-info", "Display information about a certificate");

        // TODO: Implement options and handler
        command.SetHandler(() =>
        {
            Console.WriteLine("Cert-info command - coming soon");
        });

        return command;
    }
}
