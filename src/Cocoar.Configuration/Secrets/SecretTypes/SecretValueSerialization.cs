using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocoar.Configuration.Secrets.SecretTypes;

/// <summary>
/// JSON options for (de)serializing a secret's decrypted plaintext <em>value</em> (the payload of
/// <c>Secret&lt;T&gt;</c>), kept in one place so the serialize and deserialize sides stay symmetric.
/// <para>
/// These are intentionally lenient — matching the configuration pipeline's conventions rather than the
/// stricter <see cref="JsonSerializerOptions.Default"/>:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> = true, so payloads
/// produced by external encryptors (CLI, a browser, hand-edited files) bind regardless of casing.</description></item>
/// <item><description><see cref="JsonStringEnumConverter"/>, so enums serialize as their <em>names</em>
/// (round-trip-safe if the enum is later reordered) while still <em>reading</em> both names and numbers —
/// which keeps older envelopes (enum-as-number) decryptable.</description></item>
/// </list>
/// <para>
/// Only the type-aware paths inside the library serialize a typed value (<c>Secret&lt;T&gt;</c>); anything
/// coming from outside is plain JSON, which is exactly why a name-based, case-insensitive contract is the
/// safer default here.
/// </para>
/// </summary>
internal static class SecretValueSerialization
{
    internal static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
