using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Providers; // FromStaticJson / FromStatic
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cocoar.Configuration.AspNetCore.Tests;

public sealed record TenantBillingCfg { public bool Premium { get; init; } }

// Source-generated: the generator emits the ctor taking IReactiveConfig<TenantBillingCfg> + the Config property.
public partial class TenantBillingFlags : IFeatureFlags<TenantBillingCfg>
{
    public DateTimeOffset ExpiresAt => new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public FeatureFlag<bool> PremiumEnabled => () => Config.Premium;
}

/// <summary>
/// (P6) AspNetCore tenant dimension: the per-tenant flag endpoint evaluates against the tenant in the route
/// segment, lazily warming the tenant up. Different tenants resolve different values from one rule set.
/// </summary>
public class TenantFlagEvaluationEndpointTests : IAsyncDisposable
{
    private IHost? _host;
    private HttpClient? _client;

    private async Task<HttpClient> CreateHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<TenantBillingCfg>().FromStaticJson("""{ "Premium": false }"""),
                rules.For<TenantBillingCfg>().FromStatic(a => new TenantBillingCfg { Premium = a.Tenant == "acme" }).TenantScoped(),
            ])
            .UseFeatureFlags(flags => [flags.Register<TenantBillingFlags>()])
            .UseDebounce(25));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapTenantFeatureFlagEndpoints();

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

    [Fact]
    public async Task TenantFlagEndpoint_EvaluatesPerTenant()
    {
        var client = await CreateHost();

        var acme = await client.GetAsync("/tenants/acme/flags/TenantBillingFlags/PremiumEnabled");
        Assert.Equal(HttpStatusCode.OK, acme.StatusCode);
        Assert.True((await acme.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetBoolean());

        var other = await client.GetAsync("/tenants/other/flags/TenantBillingFlags/PremiumEnabled");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.False((await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetBoolean());
    }
}
