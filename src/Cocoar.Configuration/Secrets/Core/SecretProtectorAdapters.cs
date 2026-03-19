using System.Text.Json;
using Cocoar.Configuration.Secrets.Converters;

namespace Cocoar.Configuration.Secrets.Core;

/// <summary>
/// Adapter that wraps a public ISecretDecryptor&lt;TEnvelope&gt; to work with the runtime interface.
/// Bridges the generic public API to the non-generic runtime interface.
/// </summary>
internal sealed class PublicToRuntimeDecryptorAdapter<TEnvelope> : IRuntimeSecretDecryptor
    where TEnvelope : IEncryptedEnvelope
{
    private readonly ISecretDecryptor<TEnvelope> _decryptor;

    // Shared JsonSerializerOptions with Base64UrlByteArrayConverter
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new Base64UrlByteArrayConverter() }
    };

    public PublicToRuntimeDecryptorAdapter(ISecretDecryptor<TEnvelope> decryptor)
    {
        _decryptor = decryptor ?? throw new ArgumentNullException(nameof(decryptor));
    }

    public bool CanDecrypt(string kid) => _decryptor.CanDecrypt(kid);

    public byte[] UnprotectInternal(IEncryptedEnvelope envelope, string kid)
        => _decryptor.Unprotect((TEnvelope)envelope, kid);

    public IEncryptedEnvelope DeserializeEnvelope(string json)
        => JsonSerializer.Deserialize<TEnvelope>(json, SerializerOptions)!;
}

/// <summary>
/// Adapter that wraps a public ISecretEncryptor&lt;TEnvelope&gt; to work with the runtime interface.
/// Bridges the generic public API to the non-generic runtime interface.
/// </summary>
internal sealed class PublicToRuntimeEncryptorAdapter<TEnvelope> : IRuntimeSecretEncryptor
    where TEnvelope : IEncryptedEnvelope
{
    private readonly ISecretEncryptor<TEnvelope> _encryptor;

    // Shared JsonSerializerOptions with Base64UrlByteArrayConverter
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new Base64UrlByteArrayConverter() }
    };

    public PublicToRuntimeEncryptorAdapter(ISecretEncryptor<TEnvelope> encryptor)
    {
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
    }

    public bool CanDecrypt(string kid) => _encryptor.CanDecrypt(kid);

    public IEncryptedEnvelope ProtectInternal(ReadOnlySpan<byte> plaintext, string kid)
        => _encryptor.Protect(plaintext, kid);

    public byte[] UnprotectInternal(IEncryptedEnvelope envelope, string kid)
        => _encryptor.Unprotect((TEnvelope)envelope, kid);

    public IEncryptedEnvelope DeserializeEnvelope(string json)
        => JsonSerializer.Deserialize<TEnvelope>(json, SerializerOptions)!;
}
