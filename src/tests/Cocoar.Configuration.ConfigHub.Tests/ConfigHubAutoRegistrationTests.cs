using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.ConfigHub.Tests;

/// <summary>
/// The schema ConfigHub gets is derived from the rules: each active <c>FromConfigHub</c> rule adds
/// its type at its select path; inactive rules and other providers add nothing.
/// </summary>
public sealed class ConfigHubAutoRegistrationTests
{
    private const string Url = "https://config.example/api/config/app";

    public sealed class StorageSettings
    {
        public string? Url { get; set; }
        public Secret<string>? ApiKey { get; set; }
    }

    public sealed class MailSettings
    {
        public string? Host { get; set; }
    }

    public sealed class LocalOnly
    {
        public string? Path { get; set; }
    }

    [Fact]
    public void SchemasFromRules_OnePropertyPerSelectPath_InactiveAndOtherProvidersLeftOut()
    {
        using var handler = new HubHandler();
        var options = new ConfigHubRuleOptions(Url, "tok", handler: handler);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rules =>
        [
            rules.For<StorageSettings>().FromConfigHub(options).Select("Storage"),
            rules.For<MailSettings>().FromConfigHub(options).Select("Infra:Mail"),
            rules.For<MailSettings>().FromConfigHub(options).When(_ => false).Select("Disabled"),
            rules.For<LocalOnly>().FromStaticJson("{}"),
        ]));

        var (url, token, schema) = Assert.Single(ConfigHubAutoRegistration.SchemasFromRules(manager, "App"));

        Assert.Equal((Url, "tok"), (url, token));
        var properties = schema["properties"]!.AsObject();
        Assert.Equal(["Storage", "Infra"], properties.Select(p => p.Key));
        Assert.True(properties["Storage"]!["properties"]!["ApiKey"]![SecretJsonSchema.Marker]!.GetValue<bool>());
        Assert.NotNull(properties["Infra"]!["properties"]!["Mail"]!["properties"]!["Host"]);
    }

    [Fact]
    public async Task UseConfigHubRegistration_PutsTheSchemaAfterBuild()
    {
        using var handler = new HubHandler();
        var options = new ConfigHubRuleOptions(Url, "tok", handler: handler);

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules => [rules.For<StorageSettings>().FromConfigHub(options).Select("Storage")])
            .UseConfigHubRegistration("App", "1.2.3", handler));

        var put = await handler.Put.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal($"{Url}/schema", put.Uri);
        Assert.Equal("1.2.3", (string?)JsonNode.Parse(put.Body)!["ClientVersion"]);
    }

    private sealed class HubHandler : HttpMessageHandler
    {
        public TaskCompletionSource<(string Uri, string Body)> Put { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put)
                Put.TrySetResult((request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
