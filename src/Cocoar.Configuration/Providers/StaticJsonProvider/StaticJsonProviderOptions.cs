using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.StaticJsonProvider;

public sealed class StaticJsonProviderOptions(JsonElement value) : ISourceProviderInstanceOptions
{
    public JsonElement Value { get; } = value;
    // Static value; reuse under a constant key to avoid unnecessary churn
    public string CalculateKey() => "Static";
}
