namespace Cocoar.Configuration.Flags;

/// <summary>
/// Interface for feature flag classes with typed configuration.
/// Implement this on a partial class — the source generator produces the constructor, Config property,
/// and <c>IsExpired</c> property.
/// </summary>
/// <typeparam name="TConfig">
/// The configuration type this flag class reads from, or a value tuple of types to read from several at once.
/// Unconstrained on purpose: the generator maps this straight onto <c>IReactiveConfig&lt;TConfig&gt;</c>, which is
/// equally unconstrained, and a <c>class</c> constraint would reject the documented tuple form.
/// </typeparam>
public interface IFeatureFlags<TConfig>
{
    /// <summary>
    /// When should these flags be removed from code?
    /// After this date, the health API will report them as expired.
    /// The flags continue to work — this is a cleanup reminder.
    /// </summary>
    DateTimeOffset ExpiresAt { get; }
}
