using System.Net;
using System.Text.Json.Nodes;
using Cocoar.Configuration.Secrets;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.ConfigHub.Tests;

public class ConfigHubRegistrationTests
{
    public sealed class StorageSettings
    {
        public string? Url { get; set; }
        public Secret<string>? ApiKey { get; set; }
    }

    [Fact]
    public void BuildSchema_OnePropertyPerSection_SecretsMarked()
    {
        var schema = ConfigHubRegistration.BuildSchema([new("Storage", typeof(StorageSettings))], "Shop");

        Assert.Equal("Shop", (string?)schema["title"]);
        var storage = schema["properties"]!["Storage"]!["properties"]!;
        Assert.NotNull(storage["Url"]);
        Assert.True(storage["ApiKey"]![SecretJsonSchema.Marker]!.GetValue<bool>());
    }

    [Fact]
    public async Task RegisterSchema_PutsSchemaAndClientWithTheToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        var result = await ConfigHubRegistration.RegisterSchemaAsync(client, "https://hub/api/config/app/", "tok",
            new JsonObject { ["type"] = "object" }, "Shop", "4.2026.11.0", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("https://hub/api/config/app/schema", handler.Uri);
        Assert.Equal("Bearer tok", handler.Authorization);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("4.2026.11.0", (string?)body["ClientVersion"]);
        Assert.Equal("object", (string?)body["Schema"]!["type"]);
    }

    [Fact]
    public async Task ReportEncryptionKey_PutsTheKeyDocument_RefusalIsReturned()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "invalid key");
        using var client = new HttpClient(handler);

        var result = await ConfigHubRegistration.ReportEncryptionKeyAsync(client, "https://hub/api/config/app", "tok",
            new SecretEncryptionPublicKey { Kid = "k1", PublicKey = "abc" }, CancellationToken.None);

        Assert.Equal("https://hub/api/config/app/encryption-key", handler.Uri);
        Assert.Equal("k1", (string?)JsonNode.Parse(handler.Body!)!["kid"]);
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("invalid key", result.Error);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
