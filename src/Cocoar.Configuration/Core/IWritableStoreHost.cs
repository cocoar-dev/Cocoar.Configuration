using System.Text.Json;
using Cocoar.Configuration.Rules;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

/// <summary>
/// The pipeline context a WritableStore overlay adapter needs: the merged "base" JSON below the overlay layer
/// (for sparse-write key alignment and provenance) and the effective snapshot JSON. Implemented by
/// <see cref="ConfigManager"/> (the global pipeline) and <c>TenantPipeline</c> (a tenant), so the one adapter
/// serves both global and per-tenant overlays (ADR-005 §7).
/// </summary>
internal interface IWritableStoreHost
{
    MutableJsonObject BuildBaseJson(Type configType, Func<IRuleManager, bool> isExcludedLayer);

    JsonElement? GetConfigAsJson(Type type);
}
