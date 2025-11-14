using System.Text.Json;

namespace Cocoar.Configuration.Extensibility;

/// <summary>
/// Capability interface for contributing to JSON serializer setup.
/// Implementations are retrieved from the composition and applied to configure JsonSerializerOptions.
/// </summary>
public interface ISerializerSetupCapability
{
    /// <summary>
    /// Configure the JSON serializer options (e.g., add converters).
    /// </summary>
    void Configure(JsonSerializerOptions options);
}
