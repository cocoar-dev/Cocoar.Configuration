namespace Cocoar.Configuration.DI;

/// <summary>
/// Supplies the current request/scope's tenant id for per-request configuration consumption (ADR-006 §11).
/// Ambient tenant resolution is a container/scope concern — no-DI hosts pass the tenant explicitly via the
/// <c>…ForTenant(id)</c> methods instead. The application provides a scoped implementation, typically via
/// <c>AddCocoarTenantResolver&lt;TService&gt;(...)</c> (pointing at the app's tenant service, or
/// <c>IHttpContextAccessor</c> for HTTP), or its own.
/// </summary>
public interface ITenantContext
{
    /// <summary>The current tenant id, or <see langword="null"/> when none is resolved for this scope.</summary>
    string? Current { get; }
}
