using System.Text.Json.Nodes;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.ConfigHub.Tests;

/// <summary>
/// The reported schema is derived from the types alone: what can be bound, the way every source writes it.
/// </summary>
public class ConfigHubSchemaTests
{
    public enum Provider { InMemory, Smtp, Postmark }

    public sealed class EmailSettings
    {
        public Provider Provider { get; set; }
        public Provider? Fallback { get; set; }
        public List<Provider> Allowed { get; set; } = [];
        public Dictionary<string, Provider> PerTenant { get; set; } = [];
    }

    public sealed class ApnsSettings
    {
        public string? KeyP8 { get; set; }
        public string? KeyPath { get; set; }
        public bool HasKey => !string.IsNullOrWhiteSpace(KeyP8) || !string.IsNullOrWhiteSpace(KeyPath);
    }

    public sealed record Endpoint(string Host, int Port);

    public sealed class Credentials
    {
        public string? User { get; set; }
        public Provider Via { get; set; }
        public bool HasUser => User is not null;
    }

    public sealed class StorageSettings
    {
        public required string Bucket { get; set; }
        public Secret<Credentials>? Login { get; set; }
    }

    private static JsonNode Class<T>() =>
        ConfigHubRegistration.BuildSchema([new("Class", typeof(T))], "Demo")["properties"]!["Class"]!;

    [Fact]
    public void Enums_AreListedByName_AsEverySourceWritesThem()
    {
        var properties = Class<EmailSettings>()["properties"]!;

        Assert.Equal("""["InMemory","Smtp","Postmark"]""", properties["Provider"]!["enum"]!.ToJsonString());
        Assert.Equal("""{"anyOf":[{"enum":["InMemory","Smtp","Postmark"]},{"type":"null"}]}""", properties["Fallback"]!.ToJsonString());
        Assert.Equal("""["InMemory","Smtp","Postmark"]""", properties["Allowed"]!["items"]!["enum"]!.ToJsonString());
        Assert.Equal("""["InMemory","Smtp","Postmark"]""", properties["PerTenant"]!["additionalProperties"]!["enum"]!.ToJsonString());
    }

    [Fact]
    public void ComputedProperties_AreLeftOut_ConstructorBoundOnesAreKept()
    {
        var apns = Class<ApnsSettings>()["properties"]!.AsObject();
        Assert.Equal(["KeyP8", "KeyPath"], apns.Select(p => p.Key));

        var endpoint = Class<Endpoint>()["properties"]!.AsObject();
        Assert.Equal(["Host", "Port"], endpoint.Select(p => p.Key));
    }

    [Fact]
    public void RequiredMembers_AreReported()
    {
        Assert.Equal("""["Bucket"]""", Class<StorageSettings>()["required"]!.ToJsonString());
    }

    [Fact]
    public void ObjectValuedSecrets_AreMarked_AndFollowTheSameRules()
    {
        var login = Class<StorageSettings>()["properties"]!["Login"]!;

        Assert.True(login[SecretJsonSchema.Marker]!.GetValue<bool>());
        Assert.Equal(["User", "Via"], login["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("""["InMemory","Smtp","Postmark"]""", login["properties"]!["Via"]!["enum"]!.ToJsonString());
    }
}
