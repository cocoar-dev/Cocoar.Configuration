using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.SecretTypes;

public sealed class Secret<T> : IDisposable
{
    private byte[]? _plainBytes;
    private SecretEnvelopeWrapper? _envelope;
    private SecretsDecryptorResolver? _resolver;
    private bool _disposed;
    private readonly bool _blockPlaintextAccess;

    internal Secret(T plain, SecretsDecryptorResolver? resolver = null, bool allowPlaintext = false)
    {
        var json = JsonSerializer.Serialize(plain);
        _plainBytes = Encoding.UTF8.GetBytes(json);
        _resolver = resolver;
        _blockPlaintextAccess = !allowPlaintext; // Block access if plaintext is NOT explicitly allowed
    }

    internal Secret(SecretEnvelopeWrapper envelope, SecretsDecryptorResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _envelope = envelope;
        _resolver = resolver;
        _blockPlaintextAccess = false;
    }

    public override string ToString() => "***";

    public SecretLease<T> Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ValidatePlaintextAccess();

        // Get decrypted bytes - decrypt at the LAST possible moment
        byte[] bytes;
        bool needsCleanup;

        if (_plainBytes is { } plain)
        {
            bytes = plain;
            needsCleanup = false;
        }
        else if (_envelope is { } env)
        {
            // CRITICAL: Decrypt here, right before deserialization
            bytes = DecryptEnvelope(env);
            needsCleanup = true;
        }
        else
        {
            throw new ObjectDisposedException(nameof(Secret<T>));
        }

        // Deserialize and create lease immediately after decryption
        try
        {
            return DeserializeAndCreateLease(bytes, needsCleanup);
        }
        catch
        {
            if (needsCleanup)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
            throw;
        }
    }

    private void ValidatePlaintextAccess()
    {
        if (_blockPlaintextAccess)
        {
            throw new InvalidOperationException(
                $"Secret<{typeof(T).Name}> was deserialized from plaintext JSON instead of an encrypted envelope. " +
                "Pre-encrypted envelopes are required for security. Ensure your configuration source delivers " +
                "secrets in encrypted envelope format with the '_cocoar_secret' marker.");
        }
    }

    private byte[] DecryptEnvelope(SecretEnvelopeWrapper env)
    {
        if (_resolver is null)
        {
            throw new InvalidOperationException(
                "Cannot decrypt secret: no resolver available. Ensure secrets are deserialized through ConfigManager.");
        }

        var protector = _resolver.ResolveForKid(env.Kid);
        var envelopeJson = env.Data.GetRawText();
        var envelope = protector.DeserializeEnvelope(envelopeJson);
        
        return protector.UnprotectInternal(envelope, env.Kid);
    }

    private static SecretLease<T> DeserializeAndCreateLease(byte[] bytes, bool needsCleanup)
    {
        // Special handling for byte[] type
        if (typeof(T) == typeof(byte[]))
        {
            return ByteArraySecretDeserializer.Deserialize<T>(bytes, needsCleanup);
        }

        // Standard JSON deserialization for other types
        var json = Encoding.UTF8.GetString(bytes);
        var value = JsonSerializer.Deserialize<T>(json);

        if (value is null)
        {
            // Allow null for nullable types
            if (!typeof(T).IsValueType && default(T) is null)
            {
                return new SecretLease<T>(value!, CreateCleanupAction(bytes, needsCleanup));
            }

            throw new JsonException($"Failed to deserialize secret value of type {typeof(T).Name} - result was null");
        }

        return new SecretLease<T>(value, CreateCleanupAction(bytes, needsCleanup));
    }

    private static Action CreateCleanupAction(byte[] bytes, bool needsCleanup)
    {
        return needsCleanup
            ? () => Array.Clear(bytes, 0, bytes.Length)
            : () => { };
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_plainBytes is { } bytes)
        {
            Array.Clear(bytes, 0, bytes.Length);
            _plainBytes = null;
        }
        
        _envelope = null;
        _disposed = true;
    }

    /// <summary>
    /// Creates a Secret from plaintext value. For testing/development only.
    /// Use pre-encrypted envelopes in production.
    /// </summary>
    internal static Secret<T> FromPlain(T value) => new(value, resolver: null, allowPlaintext: true);

    internal static Secret<T> FromEnvelope(JsonElement element)
    {
        if (!SecretEnvelopeWrapper.TryParse(element, out var env) || env is null)
            throw new FormatException($"Invalid secret envelope for Secret<{typeof(T).Name}>");

        return new Secret<T>(env, resolver: null);
    }
}

