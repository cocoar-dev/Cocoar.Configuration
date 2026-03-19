using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cocoar.Configuration.AspNetCore.Tests;

[Trait("Type", "Unit")]
public sealed class EntitlementEvaluationEndpointTests : IAsyncDisposable
{
    private IHost? _host;
    private HttpClient? _client;

    private async Task<HttpClient> CreateHost(
        Func<EntitlementsBuilder, EntitlementRegistration[]> entitlements,
        Func<ResolverBuilder, ResolverRegistration[]>? resolvers = null,
        string pathPrefix = "/entitlements")
    {
        var builder = WebApplication.CreateBuilder();
        if (resolvers is not null)
        {
            builder.Services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => [])
                .UseEntitlements(entitlements, resolvers));
        }
        else
        {
            builder.Services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => [])
                .UseEntitlements(entitlements));
        }

        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapEntitlementEndpoints(pathPrefix);

        await app.StartAsync();
        _host = app;
        _client = app.GetTestClient();
        return _client;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host != null) await _host.StopAsync();
        _host?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── Test types ────────────────────────────────

    public record TestRequest(string TenantId);
    public record TestContext(string TenantId);

    public class TestResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext(request.TenantId));
    }

    public class TestEntitlements : Entitlements
    {
        public Entitlement<bool> CanExport { get; }
        public Entitlement<int> MaxUsers { get; }
        public Entitlement<TestContext, bool> ContextualEntitlement { get; }

        public TestEntitlements()
        {
            CanExport = () => true;
            MaxUsers = () => 100;
            ContextualEntitlement = ctx => ctx.TenantId == "premium";
        }
    }

    // ──────────────────────────────────────────────
    // GET — no-context entitlements
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_SimpleEntitlement_ReturnsTrue()
    {
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        var response = await client.GetAsync("/entitlements/TestEntitlements/CanExport");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Get_IntEntitlement_ReturnsCorrectValue()
    {
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        var response = await client.GetAsync("/entitlements/TestEntitlements/MaxUsers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, json.GetProperty("value").GetInt32());
    }

    // ──────────────────────────────────────────────
    // POST — contextual entitlements
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualEntitlement_WithPremiumTenant_ReturnsTrue()
    {
        var client = await CreateHost(
            e => [e.Register<TestEntitlements>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsJsonAsync(
            "/entitlements/TestEntitlements/ContextualEntitlement",
            new TestRequest("premium"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Post_ContextualEntitlement_WithFreeTenant_ReturnsFalse()
    {
        var client = await CreateHost(
            e => [e.Register<TestEntitlements>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsJsonAsync(
            "/entitlements/TestEntitlements/ContextualEntitlement",
            new TestRequest("free"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("value").GetBoolean());
    }

    // ──────────────────────────────────────────────
    // 404 — unknown endpoints
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_UnknownProperty_Returns404()
    {
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        var response = await client.GetAsync("/entitlements/TestEntitlements/NonExistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownClass_Returns404()
    {
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        var response = await client.GetAsync("/entitlements/UnknownClass/CanExport");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────
    // POST — null/empty body returns 400
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualEntitlement_NullBody_Returns400()
    {
        var client = await CreateHost(
            e => [e.Register<TestEntitlements>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsync(
            "/entitlements/TestEntitlements/ContextualEntitlement",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ContextualEntitlement_EmptyBody_Throws()
    {
        // An empty body is not valid JSON, so ReadFromJsonAsync throws a JsonException.
        // TestServer re-throws server-side exceptions on the client side.
        var client = await CreateHost(
            e => [e.Register<TestEntitlements>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.PostAsync(
                "/entitlements/TestEntitlements/ContextualEntitlement",
                new StringContent("", System.Text.Encoding.UTF8, "application/json")));
    }

    // ──────────────────────────────────────────────
    // Custom path prefix
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_CustomPrefix_RoutesCorrectly()
    {
        var client = await CreateHost(
            e => [e.Register<TestEntitlements>()],
            pathPrefix: "/api/entitlements");

        var response = await client.GetAsync("/api/entitlements/TestEntitlements/CanExport");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Get_DefaultPrefix_DoesNotRouteCustomPrefix()
    {
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        // Default prefix is /entitlements, so /api/entitlements should not work
        var response = await client.GetAsync("/api/entitlements/TestEntitlements/CanExport");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────
    // POST — contextual entitlement without resolver is not mapped
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualEntitlement_WithoutResolver_Returns404()
    {
        // Register entitlement class WITHOUT a resolver — contextual entitlements should be silently skipped
        var client = await CreateHost(e => [e.Register<TestEntitlements>()]);

        var response = await client.PostAsJsonAsync(
            "/entitlements/TestEntitlements/ContextualEntitlement",
            new TestRequest("premium"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
