using System.Reflection;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Secrets;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// Registers the client with ConfigHub from what the configuration already declares: every active
/// <c>FromConfigHub</c> rule contributes the schema of its type under its class name or alias, and the
/// secrets setup contributes the public key. Opt-in with <c>UseConfigHubRegistration</c>;
/// nothing is created, only reported.
/// </summary>
public static class ConfigHubAutoRegistration
{
    private static readonly Action<ILogger, Exception?> RulesFailed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1, "RegistrationRulesFailed"), "ConfigHub registration could not resolve the current rules; retrying later");
    private static readonly Action<ILogger, Exception?> TargetFailed = LoggerMessage.Define(
        LogLevel.Warning, new EventId(2, "RegistrationTargetFailed"), "A ConfigHub registration target failed; other targets continue and this target will be retried");
    private static readonly Action<ILogger, string, int, Exception?> RegistrationRejected = LoggerMessage.Define<string, int>(
        LogLevel.Warning, new EventId(3, "RegistrationRejected"), "ConfigHub rejected registration of {Feature}: HTTP {Status}; retrying later");

    /// <summary>Starts optional capability-aware registration for the whole manager.</summary>
    /// <param name="builder">The manager being configured.</param>
    /// <param name="clientName">Optional display name, defaulting to the entry assembly's name.</param>
    /// <param name="clientVersion">Optional release, defaulting to the entry assembly's version.</param>
    /// <param name="handler">Optional caller-owned transport override.</param>
    public static ConfigManagerBuilder UseConfigHubRegistration(
        this ConfigManagerBuilder builder, string? clientName = null, string? clientVersion = null, HttpMessageHandler? handler = null)
        => builder.UseConfigHubRegistration(new ConfigHubRegistrationOptions
        { ClientName = clientName, ClientVersion = clientVersion, Handler = handler });

    /// <summary>Starts manager-wide registration using active rules and explicit lifecycle options.</summary>
    public static ConfigManagerBuilder UseConfigHubRegistration(
        this ConfigManagerBuilder builder, ConfigHubRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ClientName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientName);
        if (options.RefreshInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.RequestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        var name = options.ClientName ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "Application";
        var version = options.ClientVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        return builder.AfterBuild(manager => manager.OwnResource(new RegistrationSession(manager, options, name, version)));
    }

    /// <summary>One schema per ConfigHub endpoint, from the active rules that read from it.</summary>
    public static IReadOnlyList<(string Url, string DeliveryToken, JsonObject Schema)> SchemasFromRules(ConfigManager manager, string title)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return Targets(manager).Select(target => (target.Url, target.Token, SchemaFor(target, title))).ToList();
    }

    private sealed record Target(string Url, string Token, (ConfigRule Rule, string? Key)[] Rules, HttpMessageHandler? Handler);

    private static Target[] Targets(ConfigManager manager, bool isolateResolutionFailures = false)
    {
        var resolved = new List<(ConfigRule Rule, ConfigHubProviderQueryOptions Query, ConfigHubProviderOptions Provider)>();
        foreach (var rule in manager.Rules.Where(r => r.ProviderType == typeof(ConfigHubProvider) && IsActive(r, manager)))
        {
            try
            {
                resolved.Add((rule, (ConfigHubProviderQueryOptions)rule.ResolveQueryOptions(manager),
                    (ConfigHubProviderOptions)rule.ResolveProviderOptions(manager)));
            }
            catch (Exception) when (isolateResolutionFailures) { RulesFailed(manager.Logger, null); }
        }
        return resolved.GroupBy(r => (Url: r.Query.Url.TrimEnd('/'), Token: r.Query.DeliveryToken))
            .Select(g => new Target(g.Key.Url, g.Key.Token, g.Select(r => (r.Rule, r.Query.ClassKey)).ToArray(), g.First().Provider.Handler))
            .ToArray();
    }

    private static JsonObject SchemaFor(Target target, string title)
    {
        var properties = new JsonObject();
        var types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rule, key) in target.Rules)
        {
            if (key is null)
                throw new InvalidOperationException("ConfigHub registration requires a typed class key.");
            if (types.TryGetValue(key, out var previous) && previous != rule.ConcreteType)
                throw new InvalidOperationException($"ConfigHub class key '{key}' has conflicting types; give one an alias.");
            types[key] = rule.ConcreteType;
            var spelling = properties.Select(p => p.Key).FirstOrDefault(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            properties[spelling] = ConfigHubSchema.Export(rule.ConcreteType);
        }
        return ConfigHubRegistration.Document(title, properties);
    }

    private sealed class RegistrationSession : IDisposable, IAsyncDisposable
    {
        private readonly ConfigManager _manager;
        private readonly ConfigHubRegistrationOptions _options;
        private readonly string _name;
        private readonly string? _version;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private readonly Dictionary<(string Url, string Token, string Feature), string> _sent = new();
        private int _disposed;

        public RegistrationSession(ConfigManager manager, ConfigHubRegistrationOptions options, string name, string? version)
        {
            _manager = manager; _options = options; _name = name; _version = version;
            _worker = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    try { await RefreshAsync().ConfigureAwait(false); }
                    catch (Exception) when (!_stop.IsCancellationRequested)
                    { RulesFailed(_manager.Logger, null); }
                    await Task.Delay(_options.RefreshInterval, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
        }

        private async Task RefreshAsync()
        {
            var targets = Targets(_manager, isolateResolutionFailures: true);
            var active = targets.Select(t => (t.Url, t.Token)).ToHashSet();
            foreach (var stale in _sent.Keys.Where(k => !active.Contains((k.Url, k.Token))).ToArray()) _sent.Remove(stale);
            foreach (var target in targets)
            {
                try
                {
                    var handler = _options.Handler ?? target.Handler;
                    using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
                    client.Timeout = _options.RequestTimeout;
                    var capabilities = await ConfigHubRegistration.GetCapabilitiesAsync(client, target.Url, target.Token, _stop.Token).ConfigureAwait(false);
                    if (capabilities.Supports(ConfigHubCapabilities.Schema))
                    {
                        var schema = SchemaFor(target, _name);
                        await SendAsync(target, ConfigHubCapabilities.Schema, schema.ToJsonString() + _name + _version,
                            () => ConfigHubRegistration.RegisterSchemaAsync(client, target.Url, target.Token, schema, _name, _version, _stop.Token)).ConfigureAwait(false);
                    }
                    else _sent.Remove((target.Url, target.Token, ConfigHubCapabilities.Schema));
                    if (capabilities.Supports(ConfigHubCapabilities.EncryptionKey) && _manager.GetCurrentEncryptionKey() is { } key)
                        await SendAsync(target, ConfigHubCapabilities.EncryptionKey, key.Kid + key.PublicKey,
                            () => ConfigHubRegistration.ReportEncryptionKeyAsync(client, target.Url, target.Token, key, _stop.Token)).ConfigureAwait(false);
                    else _sent.Remove((target.Url, target.Token, ConfigHubCapabilities.EncryptionKey));
                }
                catch (Exception) when (!_stop.IsCancellationRequested)
                { TargetFailed(_manager.Logger, null); }
                _stop.Token.ThrowIfCancellationRequested();
            }
        }

        private async Task SendAsync(Target target, string feature, string content, Func<Task<ConfigHubRegistrationResult>> send)
        {
            var id = (target.Url, target.Token, feature);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            if (_sent.TryGetValue(id, out var previous) && previous == hash) return;
            var result = await send().ConfigureAwait(false);
            if (result.Succeeded) _sent[id] = hash;
            else RegistrationRejected(_manager.Logger, feature, result.StatusCode, null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
            await _worker.ConfigureAwait(false);
        }
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

}
