namespace Cocoar.Configuration.Flags;

/// <summary>
/// Interface for feature flag classes with typed configuration.
/// Implement this on a partial class — the source generator produces the constructor, Config property,
/// and <c>IsExpired</c> property.
/// </summary>
/// <typeparam name="TConfig">The configuration type (or value tuple of types) this flag class reads from.</typeparam>
public interface IFeatureFlags<TConfig> where TConfig : class
{
    /// <summary>
    /// When should these flags be removed from code?
    /// After this date, the health API will report them as expired.
    /// The flags continue to work — this is a cleanup reminder.
    /// </summary>
    DateTimeOffset ExpiresAt { get; }
}
