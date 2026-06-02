using System.Text.Json;

namespace Cocoar.Configuration.Providers.Abstractions;

public interface IProviderConfiguration
{
    private static readonly JsonSerializerOptions ProviderKeyOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    /// <summary>
    /// Generates a key used to share provider instances: rules whose options produce the <b>same</b> key share
    /// one provider instance; a <see langword="null"/> key opts out of sharing (a fresh instance per rule).
    /// <para>
    /// The default serializes these options to JSON, so two rules with value-equal options share an instance —
    /// which means that shared instance MUST be thread-safe. Return <see langword="null"/> when the options carry
    /// state that JSON can't faithfully key on or that must not be shared — e.g. a live <c>IObservable</c>, an
    /// externally-owned client/factory, or anything with reference identity (see <c>ObservableProviderOptions</c>
    /// and the <c>[JsonIgnore]</c> client-factory cases in the HTTP and writable-store options).
    /// </para>
    /// </summary>
    string? GenerateProviderKey()
    {
        return JsonSerializer.Serialize(this, GetType(), ProviderKeyOptions);
    }
}
