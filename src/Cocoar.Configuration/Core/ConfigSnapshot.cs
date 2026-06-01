namespace Cocoar.Configuration.Core;

/// <summary>
/// Immutable container for all configuration instances at a point in time.
/// Provides thread-safe, cached access to deserialized configuration objects.
/// </summary>
/// <remarks>
/// <para>
/// <b>DO NOT mutate configuration instances.</b> Instances are shared across
/// GetConfig calls and IReactiveConfig subscriptions. Mutations would affect
/// all consumers and cause inconsistent behavior.
/// </para>
/// </remarks>
public sealed class ConfigSnapshot
{
    private readonly IReadOnlyDictionary<Type, object> _instances;

    /// <summary>
    /// Monotonically increasing version number for this snapshot.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// UTC timestamp when this snapshot was created.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }

    private ConfigSnapshot(IReadOnlyDictionary<Type, object> instances, long version, DateTimeOffset timestampUtc)
    {
        _instances = instances;
        Version = version;
        TimestampUtc = timestampUtc;
    }

    /// <summary>
    /// Gets a configuration instance by type from the cached snapshot.
    /// No deserialization occurs - returns the pre-computed instance.
    /// </summary>
    /// <typeparam name="T">The configuration type to retrieve.</typeparam>
    /// <returns>The configuration instance, or null if not found.</returns>
    /// <remarks>
    /// <b>DO NOT mutate the returned instance.</b> It is shared across all consumers.
    /// </remarks>
    public T? GetConfig<T>() where T : class
    {
        if (_instances.TryGetValue(typeof(T), out var instance))
        {
            return (T)instance;
        }
        return null;
    }

    /// <summary>
    /// Gets a configuration instance by type from the cached snapshot.
    /// No deserialization occurs - returns the pre-computed instance.
    /// </summary>
    /// <param name="type">The configuration type to retrieve.</param>
    /// <returns>The configuration instance, or null if not found.</returns>
    /// <remarks>
    /// <b>DO NOT mutate the returned instance.</b> It is shared across all consumers.
    /// </remarks>
    public object? GetConfig(Type type)
    {
        return _instances.TryGetValue(type, out var instance) ? instance : null;
    }

    /// <summary>
    /// Checks if this snapshot contains a configuration of the specified type.
    /// </summary>
    public bool HasConfig<T>() where T : class => _instances.ContainsKey(typeof(T));

    /// <summary>
    /// Checks if this snapshot contains a configuration of the specified type.
    /// </summary>
    public bool HasConfig(Type type) => _instances.ContainsKey(type);

    /// <summary>
    /// Gets all configuration types registered in this snapshot.
    /// </summary>
    public IEnumerable<Type> ConfigTypes => _instances.Keys;

    /// <summary>
    /// Gets the number of configuration types in this snapshot.
    /// </summary>
    public int Count => _instances.Count;

    /// <summary>
    /// Creates a new snapshot with the specified instances.
    /// </summary>
    internal static ConfigSnapshot Create(IReadOnlyDictionary<Type, object> instances, long version)
    {
        return new ConfigSnapshot(instances, version, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// An empty snapshot with no configuration instances.
    /// </summary>
    public static ConfigSnapshot Empty { get; } = new(
        new Dictionary<Type, object>(),
        version: 0,
        timestampUtc: DateTimeOffset.MinValue);
}
