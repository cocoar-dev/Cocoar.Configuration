using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Core;

internal static partial class ConfigurationAccessorLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug,
        Message = "Fallback deserialization for {TypeName} during recompute phase")]
    public static partial void FallbackDeserialization(this ILogger logger, string typeName);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Fallback deserialization failed for {TypeName}: {Message}")]
    public static partial void FallbackDeserializationFailed(this ILogger logger, string typeName, string message);
}

internal partial class ConfigurationAccessor : IConfigurationAccessor
{
    private readonly ConfigurationState _state;
    private readonly ExposureRegistry _bindingRegistry;
    private readonly ILogger _logger;
    private readonly List<ConfigRule> _rules;
    private ConfigManagerCapabilityScope? _capabilityScope;

    public ConfigurationAccessor(
        ConfigurationState state,
        ExposureRegistry bindingRegistry,
        ILogger logger,
        List<ConfigRule> rules,
        string? tenant = null)
    {
        _state = state;
        _bindingRegistry = bindingRegistry;
        _logger = logger;
        _rules = rules;
        Tenant = tenant;
    }

    /// <inheritdoc />
    public string? Tenant { get; }

    internal void SetCapabilityScope(ConfigManagerCapabilityScope capabilityScope)
    {
        _capabilityScope = capabilityScope;
    }

    /// <summary>
    /// Gets a configuration instance from the cached snapshot.
    /// During recompute, falls back to on-demand deserialization.
    /// </summary>
    /// <remarks>
    /// <b>DO NOT mutate the returned instance.</b> It is shared across all consumers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No configuration rule is registered for type T.</exception>
    public T? GetConfig<T>() where T : class
    {
        // First try the backplane (has cached instances after initialization)
        try
        {
            var result = _state.Backplane.GetConfig(typeof(T));
            if (result is T typed)
            {
                return typed;
            }
        }
        catch (InvalidOperationException)
        {
            // Backplane not initialized yet - fall through to lazy deserialization
        }

        // Fallback: during recompute phase, deserialize on-demand from pending configurations
        return FallbackDeserialize<T>();
    }

    private T FallbackDeserialize<T>()
    {
        var type = typeof(T);

        // Try to resolve interface to concrete type
        var targetType = type;
        if (type.IsInterface && _bindingRegistry.TryGetConcreteType(type, out var concreteType))
        {
            targetType = concreteType;
        }

        if (!_state.TryGetConfiguration(targetType, out var json) || json == null)
        {
            throw NoConfigurationFor(type, targetType);
        }

        _logger.FallbackDeserialization(type.Name);

        try
        {
            byte[] bytes;
            lock (json)
            {
                bytes = MutableJsonDocument.ToUtf8Bytes(json);
            }

            using var doc = JsonDocument.Parse(bytes);
            var result = ConfigurationDeserializer.Deserialize(
                doc.RootElement,
                targetType,
                _bindingRegistry.DeserializationMap,
                _capabilityScope);

            if (result is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                $"Deserialization returned unexpected type for '{type.Name}'.");
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw our own exceptions
        }
        catch (Exception ex)
        {
            _logger.FallbackDeserializationFailed(type.Name, ex.Message);
            throw new InvalidOperationException(
                $"Failed to deserialize configuration for '{type.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds the exception thrown when no committed configuration exists for a requested type. In the global
    /// (tenant-agnostic) pipeline a type whose EVERY rule is <c>.TenantScoped()</c> contributes nothing — its
    /// rules skip when there is no tenant — so it genuinely has no global value. Surface that precisely (point
    /// the caller at the per-tenant API) instead of the generic "add a rule" message, which is misleading when
    /// a rule does exist. Mirrors the tuple guard in <see cref="Reactive.ReactiveConfigurationFactory"/>.
    /// </summary>
    private InvalidOperationException NoConfigurationFor(Type requestedType, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(Tenant) && HasOnlyTenantScopedRules(targetType))
        {
            return new InvalidOperationException(
                $"Configuration type '{requestedType.Name}' has only .TenantScoped() rules, so it has no global " +
                $"value. Read it per tenant with GetConfigForTenant<{requestedType.Name}>(tenantId) or " +
                $"GetReactiveConfigForTenant<{requestedType.Name}>(tenantId).");
        }

        return new InvalidOperationException(
            $"No configuration rule is registered for type '{requestedType.Name}'. " +
            $"Add a rule using: rules.For<{requestedType.Name}>().From...");
    }

    private bool HasOnlyTenantScopedRules(Type type)
    {
        var hasRule = false;
        foreach (var rule in _rules)
        {
            if (rule.ConcreteType != type)
            {
                continue;
            }

            hasRule = true;
            if (rule.Options?.TenantScoped != true)
            {
                return false;
            }
        }

        return hasRule;
    }

    public bool TryGetConfig<T>(out T? value) where T : class
    {
        try
        {
            value = GetConfig<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Gets configuration, throwing if not found.
    /// </summary>
    /// <remarks>
    /// This method is deprecated. GetConfig now has the same behavior - it throws if no rule is registered.
    /// </remarks>
    [Obsolete("Use GetConfig<T>() instead - it now throws if no rule is registered. " +
              "This method will be removed in a future version.")]
    public T GetRequiredConfig<T>() => (T)GetConfig(typeof(T));

    /// <summary>
    /// Gets a configuration instance from the cached snapshot.
    /// During recompute, falls back to on-demand deserialization.
    /// </summary>
    /// <remarks>
    /// <b>DO NOT mutate the returned instance.</b> It is shared across all consumers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No configuration rule is registered for the type.</exception>
    public object GetConfig(Type type)
    {
        // First try the backplane
        try
        {
            var result = _state.Backplane.GetConfig(type);
            if (result != null)
            {
                return result;
            }
        }
        catch (InvalidOperationException)
        {
            // Backplane not initialized yet - fall through to lazy deserialization
        }

        // Fallback: during recompute phase, deserialize on-demand
        return FallbackDeserialize(type);
    }

    private object FallbackDeserialize(Type type)
    {
        // Try to resolve interface to concrete type
        var targetType = type;
        if (type.IsInterface && _bindingRegistry.TryGetConcreteType(type, out var concreteType))
        {
            targetType = concreteType;
        }

        if (!_state.TryGetConfiguration(targetType, out var json) || json == null)
        {
            throw NoConfigurationFor(type, targetType);
        }

        _logger.FallbackDeserialization(type.Name);

        try
        {
            byte[] bytes;
            lock (json)
            {
                bytes = MutableJsonDocument.ToUtf8Bytes(json);
            }

            using var doc = JsonDocument.Parse(bytes);
            var result = ConfigurationDeserializer.Deserialize(
                doc.RootElement,
                targetType,
                _bindingRegistry.DeserializationMap,
                _capabilityScope);

            return result ?? throw new InvalidOperationException(
                $"Deserialization returned null for '{type.Name}'.");
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw our own exceptions
        }
        catch (Exception ex)
        {
            _logger.FallbackDeserializationFailed(type.Name, ex.Message);
            throw new InvalidOperationException(
                $"Failed to deserialize configuration for '{type.Name}': {ex.Message}", ex);
        }
    }

    public bool TryGetConfig(Type type, out object? value)
    {
        try
        {
            value = GetConfig(type);
            return true;
        }
        catch (InvalidOperationException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Gets configuration, throwing if not found.
    /// </summary>
    /// <remarks>
    /// This method is deprecated. GetConfig now has the same behavior - it throws if no rule is registered.
    /// </remarks>
    [Obsolete("Use GetConfig(Type) instead - it now throws if no rule is registered. " +
              "This method will be removed in a future version.")]
    public object GetRequiredConfig(Type type) => GetConfig(type);

    public JsonElement? GetConfigAsJson(Type type) => _state.GetConfigurationAsJson(type);
}
