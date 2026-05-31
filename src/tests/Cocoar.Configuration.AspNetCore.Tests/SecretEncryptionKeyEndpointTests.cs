using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.X509Encryption;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cocoar.Configuration.AspNetCore.Tests;

public class SecretEncryptionKeyEndpointTests : IAsyncDisposable
{
    private IHost? _host;
    private HttpClient? _client;
    private string? _pfxPath;
    private string? _certFolder;

    private const string Pattern = "/.well-known/cocoar/encryption-key";

    // ---------- single-tenant (single-kid) hosts ----------

    private async Task<HttpClient> CreateSingleTenantHost(
        string kid,
        Action<Microsoft.AspNetCore.Http.Json.JsonOptions>? configureJson = null)
    {
        _pfxPath = Path.Combine(Path.GetTempPath(), "cocoar_ep_" + Guid.NewGuid().ToString("N") + ".pfx");
        using (X509CertificateGenerator.GenerateAndSavePfx(_pfxPath, password: null, "CN=Cocoar Test", overwrite: true)) { }

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseSecretsSetup(secrets => secrets.UseCertificateFromFile(_pfxPath).WithKeyId(kid)));
        if (configureJson is not null)
            builder.Services.Configure(configureJson);
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapSecretEncryptionKey();
        return await StartAsync(app);
    }

    private async Task<HttpClient> CreateHostWithoutSecrets()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rules => []));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapSecretEncryptionKey();
        return await StartAsync(app);
    }

    private async Task<HttpClient> CreateHostWithAuth()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rules => []));
        builder.Services
            .AddAuthentication("Deny")
            .AddScheme<AuthenticationSchemeOptions, DenyAllHandler>("Deny", _ => { });
        builder.Services.AddAuthorization();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSecretEncryptionKey().RequireAuthorization();
        return await StartAsync(app);
    }

    // ---------- multi-tenant (folder, kid = tenant) host ----------

    private async Task<HttpClient> CreateTenantHost(string? resolvedTenant, params string[] tenants)
    {
        _certFolder = Path.Combine(Path.GetTempPath(), "cocoar_ep_mt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_certFolder);
        foreach (var t in tenants)
        {
            var dir = Path.Combine(_certFolder, t);
            Directory.CreateDirectory(dir);
            using (X509CertificateGenerator.GenerateAndSavePfx(Path.Combine(dir, "cert.pfx"), password: null, $"CN={t}", overwrite: true)) { }
        }

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseSecretsSetup(secrets => secrets.UseCertificatesFromFolder(_certFolder)));
        builder.Services.AddScoped<ITenantContext>(_ => new FixedTenantContext(resolvedTenant));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapTenantSecretEncryptionKey();
        return await StartAsync(app);
    }

    private async Task<HttpClient> StartAsync(WebApplication app)
    {
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
        if (_pfxPath != null && File.Exists(_pfxPath)) File.Delete(_pfxPath);
        if (_certFolder != null && Directory.Exists(_certFolder))
        {
            try { Directory.Delete(_certFolder, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ---------- single-tenant tests ----------

    [Fact]
    public async Task SingleKey_WithSecrets_Returns200WithOneKey()
    {
        const string kid = "endpoint-kid";
        var client = await CreateSingleTenantHost(kid);

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(kid, key.GetProperty("kid").GetString());
        Assert.Equal("spki", key.GetProperty("format").GetString());
        Assert.Equal("base64url", key.GetProperty("encoding").GetString());
        var publicKey = key.GetProperty("publicKey").GetString()!;
        Assert.False(string.IsNullOrEmpty(publicKey));
        Assert.DoesNotContain('=', publicKey);
    }

    [Fact]
    public async Task SingleKey_NoSecrets_Returns404()
    {
        var client = await CreateHostWithoutSecrets();

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SingleKey_RequireAuthorization_GatesTheRoute()
    {
        var client = await CreateHostWithAuth();

        var response = await client.GetAsync(Pattern);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401/403 but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task SingleKey_WithCustomJsonNamingPolicy_KeepsPinnedFieldNames()
    {
        const string kid = "policy-kid";
        var client = await CreateSingleTenantHost(
            kid,
            o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper);

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        // Every record field is pinned via [JsonPropertyName], so a non-default host naming policy
        // must NOT rename them.
        Assert.Contains("\"kid\"", raw);
        Assert.Contains("\"publicKey\"", raw);
        Assert.DoesNotContain("PUBLIC_KEY", raw);
    }

    // ---------- multi-tenant tests ----------

    [Fact]
    public async Task TenantKey_ResolvedTenant_Returns200WithOnlyThatTenantsKey()
    {
        var client = await CreateTenantHost(resolvedTenant: "tenantA", "tenantA", "tenantB");

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenantA", key.GetProperty("kid").GetString());
        Assert.False(string.IsNullOrEmpty(key.GetProperty("publicKey").GetString()));

        // Single key object — never a list / no other tenant exposed.
        Assert.Equal(JsonValueKind.Object, key.ValueKind);
    }

    [Fact]
    public async Task TenantKey_NoTenantResolved_Returns400()
    {
        var client = await CreateTenantHost(resolvedTenant: null, "tenantA");

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantKey_UnknownTenant_Returns404()
    {
        var client = await CreateTenantHost(resolvedTenant: "ghost", "tenantA");

        var response = await client.GetAsync(Pattern);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantKey_ViaHttpContextAccessorResolver_ReturnsResolvedTenantsKey()
    {
        // The HTTP transport is just AddCocoarTenantResolver<IHttpContextAccessor> — no AspNetCore-specific
        // resolver API. Here the tenant comes from a request header.
        _certFolder = Path.Combine(Path.GetTempPath(), "cocoar_ep_http_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_certFolder);
        foreach (var t in new[] { "tenantA", "tenantB" })
        {
            var dir = Path.Combine(_certFolder, t);
            Directory.CreateDirectory(dir);
            using (X509CertificateGenerator.GenerateAndSavePfx(Path.Combine(dir, "cert.pfx"), password: null, $"CN={t}", overwrite: true)) { }
        }

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules => [])
            .UseSecretsSetup(secrets => secrets.UseCertificatesFromFolder(_certFolder)));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddCocoarTenantResolver<IHttpContextAccessor>(a =>
        {
            var header = a.HttpContext?.Request.Headers["X-Tenant"].ToString();
            return string.IsNullOrEmpty(header) ? null : header;
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapTenantSecretEncryptionKey();
        var client = await StartAsync(app);

        var request = new HttpRequestMessage(HttpMethod.Get, Pattern);
        request.Headers.Add("X-Tenant", "tenantA");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var key = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenantA", key.GetProperty("kid").GetString());
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(string? current) => Current = current;

        public string? Current { get; }
    }

    private sealed class DenyAllHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public DenyAllHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}
