using System.Text.Json;

namespace Cocoar.Configuration.Extensibility;

/// <summary>
/// Allows external libraries to contribute JSON converters to the configuration deserializer.
/// </summary>
public interface ISerializerSetupContributor
{
    void Configure(JsonSerializerOptions options);
}
