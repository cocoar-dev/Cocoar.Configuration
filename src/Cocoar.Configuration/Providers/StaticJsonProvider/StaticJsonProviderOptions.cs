using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;


public record StaticJsonProviderQueryOptions() : IProviderQuery;

/// <summary>
/// Uses the default provider key (the serialized options), so the payload itself identifies the provider. A
/// config-aware <c>FromStatic</c> factory is re-evaluated every recompute, and a changed payload has to produce a
/// changed key — otherwise the lease reuses the first provider and the rule serves its startup value forever.
/// </summary>
public record StaticJsonProviderOptions(JsonElement Value) : IProviderConfiguration;
