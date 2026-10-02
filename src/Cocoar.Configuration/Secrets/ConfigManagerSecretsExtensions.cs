using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.Secrets;

public static class ConfigManagerSecretsExtensions
{
    /// <summary>
    /// The current public encryption key of the configured secrets setup, without dependency
    /// injection — e.g. to report it to a configuration service while the host is still being built.
    /// Same result as <see cref="ISecretEncryptionKeyProvider.GetCurrentKey"/>; <see langword="null"/>
    /// when no publishable key is configured.
    /// </summary>
    public static SecretEncryptionPublicKey? GetCurrentEncryptionKey(this ConfigManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return new SecretEncryptionKeyProvider(manager.CapabilityScope).GetCurrentKey();
    }
}
