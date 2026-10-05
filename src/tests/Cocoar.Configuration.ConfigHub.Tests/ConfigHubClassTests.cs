using System.Net;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.ConfigHub.Tests;

public class ConfigHubClassTests
{
    public sealed class Settings { public string Message { get; set; } = ""; }
    public sealed class RecursiveSettings { public RecursiveSettings? Next { get; set; } }
    public sealed class MailSettings { public string Host { get; set; } = ""; }
    public sealed class Outer { public Settings Inner { get; set; } = new(); }

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
    }

    [Fact]
    public void Select_ReadsTheTypeFromInsideItsClassObject_RegistrationStillDescribesTheWholeType()
    {
        using var handler = new ClassHandler("""{"Mail":{"Primary":{"Host":"smtp"}}}""");
        var options = new ConfigHubRuleOptions("https://config.example/app", "token", handler: handler, alias: "Shared");
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rule =>
            [rule.For<MailSettings>().FromConfigHub(options).Select("Mail:Primary").Required()]));

        Assert.Equal("smtp", manager.GetConfig<MailSettings>()!.Host);
        var shared = Assert.Single(ConfigHubAutoRegistration.SchemasFromRules(manager, "Demo")).Schema["properties"]!["Shared"]!;
        Assert.NotNull(shared["properties"]!["Host"]);
    }

    [Fact]
    public void MountAt_BindsTheClassObjectToANestedProperty_RegistrationStillDescribesTheWholeType()
    {
        using var handler = new ClassHandler();
        var options = new ConfigHubRuleOptions("https://config.example/app", "token", handler: handler, alias: "Inner");
        using var manager = ConfigManager.Create(c => c.UseConfiguration(rule =>
            [rule.For<Outer>().FromConfigHub(options).MountAt("Inner").Required()]));

        Assert.Equal("remote", manager.GetConfig<Outer>()!.Inner.Message);
        var inner = Assert.Single(ConfigHubAutoRegistration.SchemasFromRules(manager, "Demo")).Schema["properties"]!["Inner"]!;
        Assert.NotNull(inner["properties"]!["Inner"]!["properties"]!["Message"]);
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

    private sealed class ClassHandler(string body = """{"Message":"remote"}""") : HttpMessageHandler
    {
        public string? ClassKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ClassKey = request.Headers.GetValues("ConfigHub-Class").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
