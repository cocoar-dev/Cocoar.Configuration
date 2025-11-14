namespace Cocoar.Configuration.Configure;

/// <summary>
/// Marker interface for capabilities that require deferred execution after all setup is complete.
/// Implementations will have their Apply method called during ConfigManager.Initialize().
/// </summary>
public interface IDeferredConfiguration
{
    /// <summary>
    /// Apply the deferred configuration. Called once during ConfigManager.Initialize().
    /// </summary>
    void Apply();
}
