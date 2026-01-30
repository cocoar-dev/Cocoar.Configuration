using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Extensibility;
using Cocoar.Configuration.Secrets.Converters;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.Testing;
using Cocoar.Configuration.Testing;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets;

public sealed class SecretsBuilder : SetupDefinition
{
    private readonly Composer _composer;

    internal SecretsBuilder(ConfigManagerCapabilityScope capabilityScope)
        : base(capabilityScope)
    {
        _composer = CapabilityScope.Owner.GetRequiredComposer();

        // Register test serialization if in test context
        if (CocoarTestConfiguration.Current != null)
        {
            CocoarTestConfiguration.TestSerializerOptions ??= TestSecretSerialization.Options;
        }

        if (!_composer.Has<ISerializerSetupCapability>())
        {
            _composer.AddAs<(IDeferredConfiguration, SecretsSetupDeferredConfiguration)>(
                new SecretsSetupDeferredConfiguration(CapabilityScope));
            _composer.AddAs<ISerializerSetupCapability>(new SecretsSerializerSetup(CapabilityScope));
        }
    }

    internal override SetupDefinition Build() => this;

    internal static Composer GetComposerFor(SecretsBuilder builder) => builder._composer;

    internal static ConfigManagerCapabilityScope GetCapabilityScopeFor(SecretsBuilder builder) => builder.CapabilityScope;

    /// <summary>
    /// Conditionally allows plaintext JSON values to be deserialized into Secret&lt;T&gt;.
    /// <para>
    /// <strong>SECURITY WARNING:</strong> Only enable in development/test environments.
    /// Production configurations should always use encrypted envelopes.
    /// </para>
    /// </summary>
    /// <param name="allow">Whether to allow plaintext secrets. Defaults to <c>true</c>.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Conditional based on environment (ASP.NET Core)
    /// builder.Services.AddCocoarConfiguration(
    ///     rules => [...],
    ///     setup => [setup.Secrets().AllowPlaintext(builder.Environment.IsDevelopment())]
    /// );
    ///
    /// // Always enable for tests
    /// setup => [setup.Secrets().AllowPlaintext()]  // defaults to true
    /// </code>
    /// </example>
    public SecretsBuilder AllowPlaintext(bool allow = true)
    {
        _composer.Add(new SecretsPolicy { AllowPlaintextSecrets = allow });
        return this;
    }
}

public static class SecretsSetupExtensions
{
    public static SecretsBuilder Secrets(this SetupBuilder builder)
        => new(SetupBuilder.GetCapabilityScopeFor(builder));
}
