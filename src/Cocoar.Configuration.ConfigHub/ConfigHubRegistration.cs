using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>Outcome of a registration call: success, or why ConfigHub refused it.</summary>
public sealed record ConfigHubRegistrationResult(bool Succeeded, int StatusCode, string? Error)
{
    internal static async Task<ConfigHubRegistrationResult> FromAsync(HttpResponseMessage response, CancellationToken ct) =>
        response.IsSuccessStatusCode
            ? new(true, (int)response.StatusCode, null)
            : new(false, (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
}

/// <summary>
/// What a client tells ConfigHub about itself, next to fetching its configuration: the JSON Schema
/// of the settings it binds (so ConfigHub can render a typed form) and the public key it opens
/// secrets with (so ConfigHub can address secrets to it). Both are idempotent on the server.
/// <c>UseConfigHubRegistration</c> makes these calls from the rules.
/// </summary>
public static class ConfigHubRegistration
{
    /// <summary>Queries accepted upstream functions. An older server without this endpoint accepts none.</summary>
    public static async Task<ConfigHubCapabilities> GetCapabilitiesAsync(
        HttpClient client, string deliveryUrl, string deliveryToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{deliveryUrl.TrimEnd('/')}/capabilities");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deliveryToken);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NotImplemented)
            return new ConfigHubCapabilities(1, []);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConfigHubCapabilities>(cancellationToken: ct).ConfigureAwait(false)
            ?? new ConfigHubCapabilities(1, []);
    }

    private const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    // Settings are described as they are bound: property names unchanged.
    private static readonly JsonSerializerOptions SchemaOptions = new()
    {
        PropertyNamingPolicy = null,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// One schema for a document with one top-level property per section (e.g. <c>Storage</c> →
    /// <c>StorageSettings</c>). <see cref="Secret{T}"/> properties are marked (<see cref="SecretJsonSchema"/>).
    /// </summary>
    public static JsonObject BuildSchema(IEnumerable<KeyValuePair<string, Type>> sections, string title)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var properties = new JsonObject();
        foreach (var (name, type) in sections)
            properties[name] = SecretJsonSchema.Export(type, SchemaOptions);
        return Document(title, properties);
    }

    internal static JsonObject Document(string title, JsonObject properties)
    {
        // Exporter references are rooted at the individual type; inserting that type
        // under a class key must move its references with it (including recursive refs).
        foreach (var (key, schema) in properties)
            RebaseReferences(schema, "#/properties/" + key.Replace("~", "~0").Replace("/", "~1"));
        return new JsonObject
        {
            ["$schema"] = SchemaDialect, ["title"] = title,
            ["type"] = "object", ["properties"] = properties,
        };
    }

    private static void RebaseReferences(JsonNode? node, string prefix)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) && reference.StartsWith('#'))
                obj["$ref"] = prefix + reference[1..];
            foreach (var child in obj.Select(p => p.Value)) RebaseReferences(child, prefix);
        }
        else if (node is JsonArray array)
            foreach (var child in array) RebaseReferences(child, prefix);
    }

    /// <summary><c>PUT {deliveryUrl}/schema</c> with the client's name and version.</summary>
    public static async Task<ConfigHubRegistrationResult> RegisterSchemaAsync(
        HttpClient client, string deliveryUrl, string deliveryToken, JsonNode schema,
        string? clientName, string? clientVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var body = new JsonObject
        {
            ["Schema"] = schema.DeepClone(),
            ["ClientName"] = clientName,
            ["ClientVersion"] = clientVersion,
        };
        return await PutAsync(client, deliveryUrl, "schema", deliveryToken, JsonContent.Create(body), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>PUT {deliveryUrl}/encryption-key</c>: the public key secrets are opened with, e.g.
    /// <see cref="ConfigManagerSecretsExtensions.GetCurrentEncryptionKey"/>. Only public material.
    /// </summary>
    public static async Task<ConfigHubRegistrationResult> ReportEncryptionKeyAsync(
        HttpClient client, string deliveryUrl, string deliveryToken, SecretEncryptionPublicKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return await PutAsync(client, deliveryUrl, "encryption-key", deliveryToken, JsonContent.Create(key), ct).ConfigureAwait(false);
    }

    private static async Task<ConfigHubRegistrationResult> PutAsync(
        HttpClient client, string deliveryUrl, string path, string deliveryToken, HttpContent content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{deliveryUrl.TrimEnd('/')}/{path}") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deliveryToken);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        return await ConfigHubRegistrationResult.FromAsync(response, ct).ConfigureAwait(false);
    }
}
