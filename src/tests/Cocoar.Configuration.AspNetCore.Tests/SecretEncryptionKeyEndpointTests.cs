using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.X509Encryption;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
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

    private const string BasePattern = "/.well-known/cocoar/encryption-keys";

    /// <summary>Host with single-kid secrets configured (so a key is publishable).</summary>
    private async Task<HttpClient> CreateHostWithSecrets(
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
        app.MapSecretEncryptionKeyEndpoints();
        return await StartAsync(app);
    }

    /// <summary>Host with NO secrets configured (the accessor service is not registered).</summary>
    private async Task<HttpClient> CreateHostWithoutSecrets()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c.UseConfiguration(rules => []));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapSecretEncryptionKeyEndpoints();
        return await StartAsync(app);
    }

    /// <summary>Host that gates both routes behind authorization with a deny-all scheme.</summary>
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
        app.MapSecretEncryptionKeyEndpoints().RequireAuthorization();
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
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task List_WithSingleKidSecrets_Returns200WithOneKey()
    {
        const string kid = "endpoint-kid";
        var client = await CreateHostWithSecrets(kid);

        var response = await client.GetAsync(BasePattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = json.GetProperty("keys");
        Assert.Equal(1, keys.GetArrayLength());

        var key = keys[0];
        Assert.Equal(kid, key.GetProperty("kid").GetString());
        Assert.Equal("spki", key.GetProperty("format").GetString());
        Assert.Equal("base64url", key.GetProperty("encoding").GetString());
        var publicKey = key.GetProperty("publicKey").GetString()!;
        Assert.False(string.IsNullOrEmpty(publicKey));
        Assert.DoesNotContain('=', publicKey);
    }

    [Fact]
    public async Task ByKid_KnownKid_Returns200()
    {
        const string kid = "by-kid";
        var client = await CreateHostWithSecrets(kid);

        var response = await client.GetAsync($"{BasePattern}/{kid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(kid, json.GetProperty("kid").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("publicKey").GetString()));
    }

    [Fact]
    public async Task ByKid_UnknownKid_Returns404()
    {
        var client = await CreateHostWithSecrets("configured-kid");

        var response = await client.GetAsync($"{BasePattern}/some-other-kid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_NoSecrets_Returns200WithEmptyKeys()
    {
        var client = await CreateHostWithoutSecrets();

        var response = await client.GetAsync(BasePattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public async Task ByKid_NoSecrets_Returns404()
    {
        var client = await CreateHostWithoutSecrets();

        var response = await client.GetAsync($"{BasePattern}/anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequireAuthorization_GatesBothRoutes()
    {
        var client = await CreateHostWithAuth();

        var list = await client.GetAsync(BasePattern);
        var byKid = await client.GetAsync($"{BasePattern}/any");

        // A single .RequireAuthorization() on the composite builder must apply to BOTH routes.
        Assert.True(
            list.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"list expected 401/403 but got {(int)list.StatusCode}");
        Assert.True(
            byKid.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"by-kid expected 401/403 but got {(int)byKid.StatusCode}");
    }

    [Fact]
    public async Task List_WithCustomJsonNamingPolicy_KeepsPinnedFieldNames()
    {
        const string kid = "policy-kid";
        var client = await CreateHostWithSecrets(
            kid,
            o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper);

        var response = await client.GetAsync(BasePattern);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        // The list wrapper ("keys") and every record field are pinned via [JsonPropertyName],
        // so a non-default host naming policy must NOT rename them.
        Assert.Contains("\"keys\"", raw);
        Assert.Contains("\"kid\"", raw);
        Assert.Contains("\"publicKey\"", raw);
        Assert.DoesNotContain("KEYS", raw);
        Assert.DoesNotContain("PUBLIC_KEY", raw);
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
