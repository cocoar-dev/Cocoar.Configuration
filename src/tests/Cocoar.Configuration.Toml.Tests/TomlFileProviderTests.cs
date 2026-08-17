using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Toml;

namespace Cocoar.Configuration.Toml.Tests;

public class TomlFileProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cocoar-toml-");

    private JsonElement Parse(string toml, string filename = "config.toml")
    {
        File.WriteAllText(Path.Combine(_dir.FullName, filename), toml);
        var provider = new TomlFileProvider(new FileSourceProviderOptions(_dir.FullName));
        var bytes = provider.FetchConfigurationBytesAsync(new FileSourceProviderQueryOptions(filename))
            .GetAwaiter().GetResult();
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "TomlFileProvider")]
    public void Maps_typed_scalars_to_json_types()
    {
        var json = Parse(
            """
            name = "hello"
            enabled = true
            disabled = false
            port = 5432
            ratio = 1.5
            """);

        Assert.Equal("hello", json.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.True, json.GetProperty("enabled").ValueKind);
        Assert.Equal(JsonValueKind.False, json.GetProperty("disabled").ValueKind);
        Assert.Equal(5432, json.GetProperty("port").GetInt32());
        Assert.Equal(1.5, json.GetProperty("ratio").GetDouble());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "TomlFileProvider")]
    public void Maps_tables_arrays_and_arrays_of_tables()
    {
        var json = Parse(
            """
            hosts = ["a", "b", "c"]

            [db]
            host = "localhost"
            port = 5432

            [[servers]]
            name = "s1"

            [[servers]]
            name = "s2"
            """);

        Assert.Equal("localhost", json.GetProperty("db").GetProperty("host").GetString());
        Assert.Equal(5432, json.GetProperty("db").GetProperty("port").GetInt32());

        var hosts = json.GetProperty("hosts");
        Assert.Equal(JsonValueKind.Array, hosts.ValueKind);
        Assert.Equal(3, hosts.GetArrayLength());
        Assert.Equal("b", hosts[1].GetString());

        var servers = json.GetProperty("servers");
        Assert.Equal(JsonValueKind.Array, servers.ValueKind);
        Assert.Equal(2, servers.GetArrayLength());
        Assert.Equal("s2", servers[1].GetProperty("name").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "TomlFileProvider")]
    public void Empty_file_yields_empty_object()
    {
        Assert.Equal(JsonValueKind.Object, Parse("").ValueKind);
        Assert.Empty(Parse("").EnumerateObject());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "TomlFileProvider")]
    public void Binds_through_ConfigManager_via_FromTomlFile()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "app.toml"),
            """
            name = "myapp"
            enabled = true
            ratio = 1.5

            [db]
            port = 5432
            hosts = ["h1", "h2"]
            """);

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<AppCfg>().FromTomlFile(Path.Combine(_dir.FullName, "app.toml"))
            ]));

        var cfg = manager.GetConfig<AppCfg>()!;
        Assert.Equal("myapp", cfg.Name);
        Assert.True(cfg.Enabled);
        Assert.Equal(1.5, cfg.Ratio);
        Assert.Equal(5432, cfg.Db.Port);
        Assert.Equal(new[] { "h1", "h2" }, cfg.Db.Hosts);
    }

    public sealed class AppCfg
    {
        public string? Name { get; set; }
        public bool Enabled { get; set; }
        public double Ratio { get; set; }
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
