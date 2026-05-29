using Cocoar.Configuration.Secrets.SecretTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Extension methods for publishing the configured secrets encryption public key(s) so external
/// producers (e.g. a browser library) can build <c>cocoar.secret</c> envelopes the server decrypts.
/// Only public-key material is exposed — never a private key or plaintext.
/// </summary>
public static class SecretEncryptionKeyEndpointExtensions
{
    /// <summary>
    /// Maps a GET endpoint that lists the current encryption public key per configured kid as
    /// <c>{ "keys": [ ... ] }</c>. Always returns 200 (an empty list when no key is publishable, e.g.
    /// no secrets configured). Returns an <see cref="IEndpointConventionBuilder"/> so callers can chain
    /// <c>.RequireAuthorization()</c>. Not secured by default (matches <c>MapFeatureFlagEndpoints</c>).
    /// </summary>
    public static IEndpointConventionBuilder MapSecretEncryptionKeys(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/.well-known/cocoar/encryption-keys")
    {
        return endpoints.MapGet(pattern, (IServiceProvider sp) =>
        {
            var provider = sp.GetService<ISecretEncryptionKeyProvider>();
            var keys = provider?.GetCurrentKeys() ?? Array.Empty<SecretEncryptionPublicKey>();
            return Results.Json(new SecretEncryptionKeySet { Keys = keys });
        });
    }

    /// <summary>
    /// Maps a GET endpoint that returns the current encryption public key for a specific kid, or a
    /// 404 ProblemDetails when that kid is not currently published. Returns an
    /// <see cref="IEndpointConventionBuilder"/> for chaining <c>.RequireAuthorization()</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapSecretEncryptionKeyByKid(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/.well-known/cocoar/encryption-keys/{kid}")
    {
        return endpoints.MapGet(pattern, (string kid, IServiceProvider sp) =>
        {
            var provider = sp.GetService<ISecretEncryptionKeyProvider>();
            var key = provider?.GetCurrentKey(kid);
            return key is null
                ? Results.Problem(
                    detail: $"No published encryption key for kid '{kid}'.",
                    title: "Encryption key not found",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(key);
        });
    }

    /// <summary>
    /// Maps both the list endpoint (at <paramref name="basePattern"/>) and the by-kid endpoint
    /// (at <c>{basePattern}/{kid}</c>), returning a composite <see cref="IEndpointConventionBuilder"/>
    /// so a single <c>.RequireAuthorization()</c> (or other convention) applies to both routes.
    /// </summary>
    public static IEndpointConventionBuilder MapSecretEncryptionKeyEndpoints(
        this IEndpointRouteBuilder endpoints,
        string basePattern = "/.well-known/cocoar/encryption-keys")
    {
        var list = endpoints.MapSecretEncryptionKeys(basePattern);
        var byKid = endpoints.MapSecretEncryptionKeyByKid($"{basePattern.TrimEnd('/')}/{{kid}}");
        return new CompositeEndpointConventionBuilder(list, byKid);
    }

    private sealed class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly IEndpointConventionBuilder[] _builders;

        public CompositeEndpointConventionBuilder(params IEndpointConventionBuilder[] builders)
            => _builders = builders;

        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in _builders)
                builder.Add(convention);
        }

        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            foreach (var builder in _builders)
                builder.Finally(finallyConvention);
        }
    }
}
