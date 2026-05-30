namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Supplies the current request's tenant id for scoped, per-request configuration consumption (ADR-006 §11).
/// The application implements this — reading a claim, header, or route value; only the app knows where the
/// tenant lives — and registers it as <b>scoped</b>, or relies on the default registered by
/// <c>AddCocoarTenantReactiveConfig(resolver)</c>.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant id, or <c>null</c> when none is resolved for this request.</summary>
    string? Current { get; }
}
