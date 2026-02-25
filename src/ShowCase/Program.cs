using System.Net;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Microsoft.AspNetCore.Mvc;

namespace ShowCase;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var configManager = ConfigManager.Create(c => c
            .WithConfiguration(rule => [
                rule.For<StartUpConfiguration>().FromFile("config.json").Select("Startup"),
                rule.For<StartUpConfiguration>().FromEnvironment(),
            ])
            .WithSecretsSetup(secrets => secrets
                .UseCertificatesFromFolder("certs")));

        builder.AddCocoarConfiguration(configManager);

        var app = builder.Build();

        app.MapGet("/", () => "Hello World!");


        app.MapGet("/creds", (StartUpConfiguration conf) =>
        {
            var secret = conf.MySecret.Open().Value;
            if (!int.TryParse(secret, out var secretValue))
            {
                return $"Invalid secret format: expected integer, got '{secret}'";
            }
            var networkCredsConfig = conf.Credentials.Open().Value;
            var networkCreds = networkCredsConfig.ToNetworkCredential();
            return $"Username: {networkCreds.UserName}, Domain: {networkCreds.Domain}, Password Length: {networkCreds.Password.Length}";
        });

        app.Run();
    }
}


public class NetworkCredentialConfig
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";  // Encrypt this!
    public string? Domain { get; set; }

    public NetworkCredential ToNetworkCredential()
        => new(UserName, Password, Domain);
}
