using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public interface IProviderConfiguration
{
    string GenerateProviderKey()
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
