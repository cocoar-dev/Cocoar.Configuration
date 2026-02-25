using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Secrets;

public static class SecretsBuilderExtensions
{
    /// <summary>
    /// Configures secrets setup (encryption, certificates, plaintext policy).
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .WithConfiguration(rules => [...])
    ///     .WithSecretsSetup(secrets => secrets.AllowPlaintext()));
    ///
    /// ConfigManager.Create(c => c
    ///     .WithConfiguration(rules => [...])
    ///     .WithSecretsSetup(secrets => secrets
    ///         .UseCertificateFromFile("cert.pfx")
    ///         .WithKeyId("my-key")));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder WithSecretsSetup(
        this ConfigManagerBuilder builder,
        Func<SecretsBuilder, SetupDefinition> configure)
    {
        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        var secretsBuilder = new SecretsBuilder(scope);
        var result = configure(secretsBuilder);
        result.Build();
        return builder;
    }
}
