using System.Text.Json;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Provides access to configuration snapshots.
/// Used by rule factories to reference earlier configuration state when building dependent rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>DO NOT mutate configuration instances.</b> All GetConfig methods return cached,
/// shared instances. Mutations would affect all consumers and cause inconsistent behavior.
/// </para>
/// </remarks>
public interface IConfigurationAccessor
{
    /// <summary>
    /// Gets a configuration instance from the cached snapshot.
    /// </summary>
    /// <remarks>
    /// After initialization completes, this method returns the cached instance for registered types.
    /// Throws <see cref="InvalidOperationException"/> if no rule is registered for the type.
    /// <para><b>DO NOT mutate the returned instance.</b></para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No configuration rule is registered for type T.</exception>
    T? GetConfig<T>() where T : class;

    /// <summary>
    /// Tries to get a configuration instance without throwing.
    /// </summary>
    /// <returns>True if configuration exists for the type; false otherwise.</returns>
    bool TryGetConfig<T>(out T? value) where T : class;

    /// <summary>
    /// Gets a configuration instance from the cached snapshot.
    /// </summary>
    /// <remarks>
    /// After initialization completes, this method returns the cached instance for registered types.
    /// Throws <see cref="InvalidOperationException"/> if no rule is registered for the type.
    /// <para><b>DO NOT mutate the returned instance.</b></para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No configuration rule is registered for the type.</exception>
    object GetConfig(Type type);

    /// <summary>
    /// Tries to get a configuration instance without throwing.
    /// </summary>
    /// <returns>True if configuration exists for the type; false otherwise.</returns>
    bool TryGetConfig(Type type, out object? value);

    JsonElement? GetConfigAsJson(Type type);

    /// <summary>
    /// The tenant this accessor resolves configuration for, or <c>null</c> in the global (tenant-agnostic) pipeline.
    /// Tenant-scoped rules (<c>.TenantScoped()</c>) are skipped when this is null/empty; tenant-varying rule
    /// factories interpolate it (e.g. <c>FromFile(a =&gt; $"db.{a.Tenant}.json")</c>).
    /// </summary>
    /// <remarks>Default interface member (returns <c>null</c>) so existing implementers are not broken.</remarks>
    string? Tenant => null;
}
