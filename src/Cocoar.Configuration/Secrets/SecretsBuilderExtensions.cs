using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Testing;

namespace Cocoar.Configuration.Secrets;

public static class SecretsBuilderExtensions
{
    /// <summary>
    /// Configures secrets setup (encryption, certificates, plaintext policy).
    /// When a test context with a secrets setup override is active, the override is used instead.
    /// </summary>
    /// <example>
    /// <code>
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...])
    ///     .UseSecretsSetup(secrets => secrets.AllowPlaintext()));
    ///
    /// ConfigManager.Create(c => c
    ///     .UseConfiguration(rules => [...])
    ///     .UseSecretsSetup(secrets => secrets
    ///         .UseCertificateFromFile("cert.pfx")
    ///         .WithKeyId("my-key")));
    /// </code>
    /// </example>
    public static ConfigManagerBuilder UseSecretsSetup(
        this ConfigManagerBuilder builder,
        Func<SecretsBuilder, SetupDefinition> configure)
    {
        var scope = ConfigManagerBuilder.GetCapabilityScope(builder);
        var secretsBuilder = new SecretsBuilder(scope);

        var testContext = CocoarTestConfiguration.Current;
        var effectiveConfigure = testContext?.GetSecretsSetupOverride() ?? configure;

        var result = effectiveConfigure(secretsBuilder);
        result.Build();
        return builder;
    }

    /// <summary>
    /// Replaces the secrets setup used during ConfigManager initialization in tests.
    /// Extends <see cref="TestOverrideBuilder"/> so that it can be chained fluently alongside
    /// <c>ReplaceConfiguration</c> / <c>AppendConfiguration</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// using var _ = CocoarTestConfiguration
    ///     .ReplaceConfiguration(rules => [...])
    ///     .ReplaceSecretsSetup(secrets => secrets.AllowPlaintext());
    /// </code>
    /// </example>
    public static TestOverrideBuilder ReplaceSecretsSetup(
        this TestOverrideBuilder builder,
        Func<SecretsBuilder, SetupDefinition> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        builder.SetSecretsSetupOverride(configure);
        return builder;
    }
}

internal static class TestConfigurationContextSecretsExtensions
{
    /// <summary>
    /// Returns the secrets setup override cast to the correct strongly-typed delegate,
    /// or null if no override has been registered.
    /// </summary>
    internal static Func<SecretsBuilder, SetupDefinition>? GetSecretsSetupOverride(
        this TestConfigurationContext context)
        => context.SecretsSetupOverride as Func<SecretsBuilder, SetupDefinition>;
}
