using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public interface IProviderConfiguration
{
    /// Generates a provider key for instance sharing. 
    /// Return null to indicate this provider should never be reused/shared.
    /// Return the same key to share provider instances with the same key.
    string? GenerateProviderKey()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            PropertyNamingPolicy = null,
            WriteIndented = false
        };
        return JsonSerializer.Serialize(this, GetType(), options);
    }
}
