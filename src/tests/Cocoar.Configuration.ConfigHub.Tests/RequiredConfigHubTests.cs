using System.Net;
using System.Net.Http.Headers;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.ConfigHub.Tests;

public sealed class RequiredConfigHubTests
{
    [Fact]
    public void RequiredRuleRejectsInvalidDeliveryCredentialsDuringStartup()
    {
        using var handler = new UnauthorizedHandler();

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var manager = ConfigManager.Create(configuration => configuration
                .UseConfiguration(rules =>
                [
                    rules.For<RemoteSettings>().FromStaticJson("""{ "Value": "local" }"""),
                    rules.For<RemoteSettings>().FromConfigHub(
                            "https://config.example/api/config/demo-app",
                            "chcfg_wrong",
                            handler: handler)
                        .Required(),
                ]));
        });

        Assert.Contains("401", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("chcfg_wrong", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("chcfg_wrong", handler.Authorization?.Parameter);
    }

    private sealed class RemoteSettings
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
