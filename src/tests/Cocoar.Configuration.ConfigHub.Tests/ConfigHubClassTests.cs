using System.Net;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.ConfigHub.Tests;

public class ConfigHubClassTests
{
    public sealed class Settings { public string Message { get; set; } = ""; }
    public sealed class RecursiveSettings { public RecursiveSettings? Next { get; set; } }

    [Fact]
    public void SchemaReferences_StayInsideTheirClassObject()
    {
        var schema = ConfigHubRegistration.BuildSchema(new Dictionary<string, Type> { ["A/B"] = typeof(RecursiveSettings) }, "Demo");
        Assert.Contains("#/properties/A~1B", schema.ToJsonString());
        Assert.DoesNotContain("\"$ref\":\"#\"", schema.ToJsonString());
    }

    [Theory]
    [InlineData(null, "Settings")]
    [InlineData("Mail settings", "Mail settings")]
    public void TypedRule_UsesTheSameClassKeyForDeliveryAndRegistration(string? alias, string expected)
    {
        using var handler = new ClassHandler();
        var options = new ConfigHubRuleOptions("https://config.example/app", "token", handler: handler, alias: alias)
            .WithDimension("server", "host").WithWarnings(ConfigHubWarningMode.Warn);
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rule => [rule.For<Settings>().FromConfigHub(options).Required()]));
        Assert.Equal("remote", manager.GetConfig<Settings>()!.Message);
        Assert.Equal(expected, Uri.UnescapeDataString(handler.ClassKey!));
        var schema = Assert.Single(ConfigHubAutoRegistration.SchemasFromRules(manager, "Demo")).Schema;
        Assert.NotNull(schema["properties"]![expected]!["properties"]!["Message"]);
        Assert.DoesNotContain(typeof(ConfigHubRuleBuilder).GetMethods(), method => method.Name is "Select" or "MountAt");
    }

    [Fact]
    public void ClassKey_IsPartOfTheQueryIdentity()
    {
        var a = new ConfigHubProviderQueryOptions("https://config.example/app", "secret", classKey: "A");
        var b = new ConfigHubProviderQueryOptions("https://config.example/app", "secret", classKey: "B");
        var json = System.Text.Json.JsonSerializer.Serialize(a);
        Assert.NotEqual(json, System.Text.Json.JsonSerializer.Serialize(b));
        Assert.DoesNotContain("secret", json);
    }

    private sealed class ClassHandler : HttpMessageHandler
    {
        public string? ClassKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ClassKey = request.Headers.GetValues("ConfigHub-Class").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"Message":"remote"}""") });
        }
    }
}
