namespace Cocoar.Configuration.Secrets.Core;

public interface IEncryptedEnvelope
{
    byte[] Ciphertext { get; }
}

internal interface IRuntimeSecretDecryptor
{
    bool CanDecrypt(string kid);
    byte[] UnprotectInternal(IEncryptedEnvelope envelope, string kid);
    IEncryptedEnvelope DeserializeEnvelope(string json);
}

internal interface IRuntimeSecretEncryptor : IRuntimeSecretDecryptor
{
    IEncryptedEnvelope ProtectInternal(ReadOnlySpan<byte> plaintext, string kid);
}

public interface ISecretDecryptor<TEnvelope> where TEnvelope : IEncryptedEnvelope
{
    bool CanDecrypt(string kid);
    byte[] Unprotect(TEnvelope envelope, string kid);
}

internal interface ISecretEncryptor<TEnvelope> : ISecretDecryptor<TEnvelope> where TEnvelope : IEncryptedEnvelope
{
    TEnvelope Protect(ReadOnlySpan<byte> plaintext, string kid);
}
