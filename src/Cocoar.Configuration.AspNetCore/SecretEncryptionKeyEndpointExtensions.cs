using Cocoar.Configuration.DI;
using Cocoar.Configuration.Secrets.SecretTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Extension methods for publishing the configured secrets encryption public key so external
/// producers (e.g. a browser library) can build <c>cocoar.secret</c> envelopes the server decrypts.
/// Only public-key material is exposed — never a private key or plaintext. Each endpoint returns
/// exactly ONE key (never a list), so one tenant's key can never expose another's.
/// </summary>
public static class SecretEncryptionKeyEndpointExtensions
{
    private const string DefaultPattern = "/.well-known/cocoar/encryption-key";

    /// <summary>
    /// Maps a GET endpoint returning the current encryption public key for a SINGLE-TENANT deployment,
    /// or 404 ProblemDetails when none is published. Returns an <see cref="IEndpointConventionBuilder"/>
    /// so callers can chain <c>.RequireAuthorization()</c>. Not secured by default (matches
    /// <c>MapFeatureFlagEndpoints</c>).
    /// </summary>
    public static IEndpointConventionBuilder MapSecretEncryptionKey(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        return endpoints.MapGet(pattern, (IServiceProvider sp) =>
        {
            var key = sp.GetService<ISecretEncryptionKeyProvider>()?.GetCurrentKey();
            return key is null ? KeyNotFound() : Results.Json(key);
        });
    }

    /// <summary>
    /// Maps a GET endpoint returning the current encryption public key for the tenant the request
    /// already resolves to. The tenant is read from <see cref="ITenantContext.Current"/> (auth, subdomain,
    /// or route — supplied by the app, never a client-chosen value), and ONLY that tenant's single
    /// current key is returned (404 ProblemDetails when none, 400 when no tenant is resolved). Returns
    /// an <see cref="IEndpointConventionBuilder"/> for chaining <c>.RequireAuthorization()</c>.
    /// <para>The app must register a scoped <see cref="ITenantContext"/> — e.g. via
    /// <c>AddCocoarTenantResolver&lt;TService&gt;(...)</c> or its own implementation.</para>
    /// </summary>
    public static IEndpointConventionBuilder MapTenantSecretEncryptionKey(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPattern)
    {
        return endpoints.MapGet(pattern, (HttpContext http) =>
        {
            var sp = http.RequestServices;
            var tenant = sp.GetService<ITenantContext>()?.Current;
            if (string.IsNullOrWhiteSpace(tenant))
            {
                return Results.Problem(
                    detail: "No tenant is resolved for this request.",
                    title: "Tenant not resolved",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var key = sp.GetService<ISecretEncryptionKeyProvider>()?.GetCurrentKeyForTenant(tenant);
            return key is null ? KeyNotFound() : Results.Json(key);
        });
    }

    private static IResult KeyNotFound()
        => Results.Problem(
            detail: "No encryption key is currently published.",
            title: "Encryption key not found",
            statusCode: StatusCodes.Status404NotFound);
}
