using System.Text.Json.Serialization;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

public sealed record HybridEnvelope : IEncryptedEnvelope
{
    [JsonPropertyName("wk")]
    public required byte[] WrappedKey { get; init; }

    [JsonPropertyName("walg")]
    public required string WrappingAlgorithm { get; init; }

    [JsonPropertyName("iv")]
    public required byte[] Iv { get; init; }

    [JsonPropertyName("ct")]
    public required byte[] Ciphertext { get; init; }

    [JsonPropertyName("tag")]
    public required byte[] Tag { get; init; }
}
