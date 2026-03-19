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

public class FlagEvaluationEndpointTests : IAsyncDisposable
{
    private IHost? _host;
    private HttpClient? _client;

    private async Task<HttpClient> CreateHost(
        Func<FlagsBuilder, FlagRegistration[]> flags,
        Func<ResolverBuilder, ResolverRegistration[]>? resolvers = null,
        string pathPrefix = "/flags")
    {
        var builder = WebApplication.CreateBuilder();
        if (resolvers is not null)
        {
            builder.Services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => [])
                .UseFeatureFlags(flags, resolvers));
        }
        else
        {
            builder.Services.AddCocoarConfiguration(c => c
                .UseConfiguration(rules => [])
                .UseFeatureFlags(flags));
        }

        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapFeatureFlagEndpoints(pathPrefix);

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

    public record TestRequest(string UserId);
    public record TestContext(string UserId);

    public class TestResolver : IContextResolver<TestRequest, TestContext>
    {
        public Task<TestContext> ResolveAsync(TestRequest request)
            => Task.FromResult(new TestContext(request.UserId));
    }

    public class TestFlags
    {
        public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public FeatureFlag<bool> SimpleFeatureFlag { get; }
        public FeatureFlag<int> IntFeatureFlag { get; }
        public FeatureFlag<TestContext, bool> ContextualFeatureFlag { get; }

        public TestFlags()
        {
            SimpleFeatureFlag = () => true;
            IntFeatureFlag = () => 42;
            ContextualFeatureFlag = ctx => ctx.UserId == "admin";
        }
    }

    // ──────────────────────────────────────────────
    // GET — no-context flags
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_SimpleFlag_ReturnsTrue()
    {
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        var response = await client.GetAsync("/flags/TestFlags/SimpleFeatureFlag");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Get_IntFlag_ReturnsCorrectValue()
    {
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        var response = await client.GetAsync("/flags/TestFlags/IntFeatureFlag");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42, json.GetProperty("value").GetInt32());
    }

    // ──────────────────────────────────────────────
    // POST — contextual flags
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualFlag_WithAdmin_ReturnsTrue()
    {
        var client = await CreateHost(
            flags => [flags.Register<TestFlags>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsJsonAsync(
            "/flags/TestFlags/ContextualFeatureFlag",
            new TestRequest("admin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Post_ContextualFlag_WithNonAdmin_ReturnsFalse()
    {
        var client = await CreateHost(
            flags => [flags.Register<TestFlags>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsJsonAsync(
            "/flags/TestFlags/ContextualFeatureFlag",
            new TestRequest("user123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("value").GetBoolean());
    }

    // ──────────────────────────────────────────────
    // 404 — unknown endpoints
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_UnknownFlag_Returns404()
    {
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        var response = await client.GetAsync("/flags/TestFlags/NonExistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownClass_Returns404()
    {
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        var response = await client.GetAsync("/flags/UnknownClass/SimpleFeatureFlag");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────
    // POST — null/empty body returns 400
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualFlag_NullBody_Returns400()
    {
        var client = await CreateHost(
            flags => [flags.Register<TestFlags>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        var response = await client.PostAsync(
            "/flags/TestFlags/ContextualFeatureFlag",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ContextualFlag_EmptyBody_Throws()
    {
        // An empty body is not valid JSON, so ReadFromJsonAsync throws a JsonException.
        // TestServer re-throws server-side exceptions on the client side.
        var client = await CreateHost(
            flags => [flags.Register<TestFlags>()],
            resolvers => [resolvers.Global<TestResolver>()]);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.PostAsync(
                "/flags/TestFlags/ContextualFeatureFlag",
                new StringContent("", System.Text.Encoding.UTF8, "application/json")));
    }

    // ──────────────────────────────────────────────
    // Custom path prefix
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Get_CustomPrefix_RoutesCorrectly()
    {
        var client = await CreateHost(
            flags => [flags.Register<TestFlags>()],
            pathPrefix: "/api/feature-flags");

        var response = await client.GetAsync("/api/feature-flags/TestFlags/SimpleFeatureFlag");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task Get_DefaultPrefix_DoesNotRouteCustomPrefix()
    {
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        // Default prefix is /flags, so /api/feature-flags should not work
        var response = await client.GetAsync("/api/feature-flags/TestFlags/SimpleFeatureFlag");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──────────────────────────────────────────────
    // POST — contextual flag without resolver is not mapped
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Post_ContextualFlag_WithoutResolver_Returns404()
    {
        // Register flag class WITHOUT a resolver — contextual flags should be silently skipped
        var client = await CreateHost(flags => [flags.Register<TestFlags>()]);

        var response = await client.PostAsJsonAsync(
            "/flags/TestFlags/ContextualFeatureFlag",
            new TestRequest("admin"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
