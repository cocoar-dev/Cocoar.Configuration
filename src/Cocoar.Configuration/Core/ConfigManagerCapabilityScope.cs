using Cocoar.Capabilities;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Strongly-typed capability scope for ConfigManager.
/// Provides compile-time type safety for owner operations.
/// </summary>
public sealed class ConfigManagerCapabilityScope : CapabilityScope<ConfigManager>
{
    public ConfigManagerCapabilityScope(ConfigManager owner) : base(owner)
    {
    }
    
    public ConfigManagerCapabilityScope(ConfigManager owner, CapabilityScopeOptions? options) : base(owner, options)
    {
    }
}
