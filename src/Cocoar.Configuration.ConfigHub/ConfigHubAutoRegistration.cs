using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Secrets;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Registers the client with ConfigHub from what the configuration already declares: every active
/// <c>FromConfigHub</c> rule contributes the schema of its type at its <c>Select</c> path, and the
/// secrets setup contributes the public key. Opt-in with <see cref="UseConfigHubRegistration"/>;
/// nothing is created, only reported.
/// </summary>
public static class ConfigHubAutoRegistration
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // Settings are described as they are bound: property names unchanged.
    private static readonly JsonSerializerOptions SchemaOptions = new()
    {
        PropertyNamingPolicy = null,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>
    /// After the configuration is built, reports to each ConfigHub endpoint the rules use: the JSON
    /// Schema of the settings bound from it and, when secrets are set up, the public key. Runs in the
    /// background; a refusal or an unreachable hub is logged, never thrown.
    /// </summary>
    /// <param name="clientName">Shown in ConfigHub, e.g. the product name.</param>
    /// <param name="clientVersion">Release the client runs; defaults to the entry assembly's version.</param>
    /// <param name="handler">Optional caller-owned HTTP handler (mTLS, proxy). Not disposed.</param>
    public static ConfigManagerBuilder UseConfigHubRegistration(
        this ConfigManagerBuilder builder, string clientName, string? clientVersion = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var version = clientVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        return builder.AfterBuild(manager => _ = RegisterAsync(manager, clientName, version, handler));
    }

    /// <summary>One schema per ConfigHub endpoint, from the active rules that read from it.</summary>
    public static IReadOnlyList<(string Url, string DeliveryToken, JsonObject Schema)> SchemasFromRules(ConfigManager manager, string title)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var byEndpoint = new Dictionary<(string Url, string Token), JsonObject>();
        foreach (var rule in manager.Rules.Where(r => r.ProviderType == typeof(ConfigHubProvider) && IsActive(r, manager)))
        {
            if (rule.ResolveQueryOptions(manager) is not ConfigHubProviderQueryOptions query)
                continue;
            if (!byEndpoint.TryGetValue((query.Url, query.DeliveryToken), out var properties))
                byEndpoint[(query.Url, query.DeliveryToken)] = properties = [];
            Place(properties, rule.Options?.SelectPath, SecretJsonSchema.Export(rule.ConcreteType, SchemaOptions));
        }

        return byEndpoint.Select(e => (e.Key.Url, e.Key.Token, ConfigHubRegistration.Document(title, e.Value))).ToList();
    }

    private static async Task RegisterAsync(ConfigManager manager, string clientName, string? clientVersion, HttpMessageHandler? handler)
    {
        var log = manager.Logger;
        try
        {
            using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout;
            var key = manager.GetCurrentEncryptionKey();
            foreach (var (url, token, schema) in SchemasFromRules(manager, clientName))
            {
                Report(log, "schema", url, await ConfigHubRegistration.RegisterSchemaAsync(client, url, token, schema, clientName, clientVersion).ConfigureAwait(false));
                if (key is not null)
                    Report(log, "encryption key", url, await ConfigHubRegistration.ReportEncryptionKeyAsync(client, url, token, key).ConfigureAwait(false));
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Registering with ConfigHub failed");
        }
    }

    private static void Report(ILogger log, string what, string url, ConfigHubRegistrationResult result)
    {
        if (!result.Succeeded)
            log.LogWarning("ConfigHub {Url} rejected the {What}: {Status} {Error}", url, what, result.StatusCode, result.Error);
    }

    private static bool IsActive(ConfigRule rule, ConfigManager manager)
    {
        try
        {
            return (rule.Options?.UseWhen?.Invoke(manager) ?? true) && (rule.Options?.ActivationGate?.Invoke(manager) ?? true);
        }
        catch (Exception)
        {
            return false; // a gate that cannot be evaluated yet: the rule does not read from the hub now
        }
    }

    /// <summary>Puts a type's schema at its select path (<c>A:B</c> nests); without a path its members go to the root.</summary>
    private static void Place(JsonObject properties, string? selectPath, JsonNode schema)
    {
        if (selectPath is null)
        {
            if (schema["properties"] is JsonObject members)
                foreach (var (name, member) in members)
                    properties[name] = member?.DeepClone();
            return;
        }

        var segments = selectPath.Split(':');
        var current = properties;
        foreach (var segment in segments[..^1])
        {
            if (current[segment] is not JsonObject { } holder || holder["properties"] is not JsonObject nested)
            {
                nested = [];
                current[segment] = new JsonObject { ["type"] = "object", ["properties"] = nested };
            }
            current = nested;
        }
        current[segments[^1]] = schema;
    }
}
