using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Shared merge policy for the configuration pipeline: layer merging matches property names
/// <em>case-insensitively</em>. This is consistent with how the effective config is read back
/// (System.Text.Json case-insensitive, like <c>IConfiguration</c>), and it removes the need to align an
/// overlay's key casing to the lower layers at write time — a layer's own casing no longer matters.
/// </summary>
internal static class ConfigMergeOptions
{
    internal static readonly MutableJsonMergeOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}
