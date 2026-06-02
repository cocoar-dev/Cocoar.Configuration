using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Yaml;

namespace Cocoar.Configuration.Yaml.Tests;

public class YamlFileProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cocoar-yaml-");

    private JsonElement Parse(string yaml, string filename = "config.yaml")
    {
        File.WriteAllText(Path.Combine(_dir.FullName, filename), yaml);
        var provider = new YamlFileProvider(new FileSourceProviderOptions(_dir.FullName));
        var bytes = provider.FetchConfigurationBytesAsync(new FileSourceProviderQueryOptions(filename))
            .GetAwaiter().GetResult();
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "YamlFileProvider")]
    public void Infers_scalar_types_for_plain_scalars()
    {
        var json = Parse(
            """
            enabled: true
            disabled: false
            port: 5432
            ratio: 1.5
            missing: null
            name: hello
            quoted: "true"
            """);

        Assert.Equal(JsonValueKind.True, json.GetProperty("enabled").ValueKind);
        Assert.Equal(JsonValueKind.False, json.GetProperty("disabled").ValueKind);
        Assert.Equal(5432, json.GetProperty("port").GetInt32());
        Assert.Equal(1.5, json.GetProperty("ratio").GetDouble());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missing").ValueKind);
        Assert.Equal("hello", json.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("quoted").ValueKind); // quoted stays a string
        Assert.Equal("true", json.GetProperty("quoted").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "YamlFileProvider")]
    public void Maps_nested_objects_and_sequences()
    {
        var json = Parse(
            """
            db:
              host: localhost
              port: 5432
            hosts:
              - a
              - b
              - c
            """);

        Assert.Equal("localhost", json.GetProperty("db").GetProperty("host").GetString());
        Assert.Equal(5432, json.GetProperty("db").GetProperty("port").GetInt32());
        var hosts = json.GetProperty("hosts");
        Assert.Equal(JsonValueKind.Array, hosts.ValueKind);
        Assert.Equal(3, hosts.GetArrayLength());
        Assert.Equal("b", hosts[1].GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "YamlFileProvider")]
    public void Empty_file_yields_empty_object()
    {
        Assert.Equal(JsonValueKind.Object, Parse("").ValueKind);
        Assert.Empty(Parse("").EnumerateObject());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "YamlFileProvider")]
    public void Binds_through_ConfigManager_via_FromYamlFile()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "app.yaml"),
            """
            name: myapp
            enabled: true
            ratio: 1.5
            note: "true"
            db:
              port: 5432
              hosts:
                - h1
                - h2
            """);

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<AppCfg>().FromYamlFile(Path.Combine(_dir.FullName, "app.yaml"))
            ]));

        var cfg = manager.GetConfig<AppCfg>()!;
        Assert.Equal("myapp", cfg.Name);
        Assert.True(cfg.Enabled);
        Assert.Equal(1.5, cfg.Ratio);
        Assert.Equal("true", cfg.Note);
        Assert.Equal(5432, cfg.Db.Port);
        Assert.Equal(new[] { "h1", "h2" }, cfg.Db.Hosts);
    }

    public sealed class AppCfg
    {
        public string? Name { get; set; }
        public bool Enabled { get; set; }
        public double Ratio { get; set; }
        public string? Note { get; set; }
        public DbCfg Db { get; set; } = new();
    }

    public sealed class DbCfg
    {
        public int Port { get; set; }
        public List<string> Hosts { get; set; } = new();
    }

    public void Dispose()
    {
        try { _dir.Delete(recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
