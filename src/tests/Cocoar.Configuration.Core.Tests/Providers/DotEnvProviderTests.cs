using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Providers;

public class DotEnvProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cocoar-dotenv-");

    private JsonElement Parse(string content, string filename = ".env")
    {
        File.WriteAllText(Path.Combine(_dir.FullName, filename), content);
        var provider = new DotEnvProvider(new FileSourceProviderOptions(_dir.FullName));
        var bytes = provider.FetchConfigurationBytesAsync(new FileSourceProviderQueryOptions(filename))
            .GetAwaiter().GetResult();
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "DotEnvProvider")]
    public void Parses_keys_comments_blanks_and_export_prefix()
    {
        var json = Parse(
            """
            # a comment
            NAME=myapp

            export TOKEN=abc123
            """);

        Assert.Equal("myapp", json.GetProperty("NAME").GetString());
        Assert.Equal("abc123", json.GetProperty("TOKEN").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "DotEnvProvider")]
    public void Nests_keys_on_double_underscore_and_colon()
    {
        var json = Parse(
            """
            Db__Port=5432
            Db:Host=localhost
            """);

        Assert.Equal("5432", json.GetProperty("Db").GetProperty("Port").GetString());
        Assert.Equal("localhost", json.GetProperty("Db").GetProperty("Host").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "DotEnvProvider")]
    public void Handles_quotes_and_inline_comments()
    {
        var json = Parse(
            """
            DQ="hello world"
            SQ='literal $x'
            ESC="line1\nline2"
            INLINE=value # trailing comment
            HASHVALUE=pa#ss
            """);

        Assert.Equal("hello world", json.GetProperty("DQ").GetString());
        Assert.Equal("literal $x", json.GetProperty("SQ").GetString());        // single-quote = literal, no escapes
        Assert.Equal("line1\nline2", json.GetProperty("ESC").GetString());     // double-quote unescapes \n
        Assert.Equal("value", json.GetProperty("INLINE").GetString());         // trailing ' #...' stripped
        Assert.Equal("pa#ss", json.GetProperty("HASHVALUE").GetString());      // '#' without leading space kept
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "DotEnvProvider")]
    public void Empty_or_keyless_lines_are_ignored()
    {
        var json = Parse(
            """
            =novalue
            JUSTAKEY
            REAL=1
            """);

        Assert.False(json.TryGetProperty("JUSTAKEY", out _));
        Assert.Equal("1", json.GetProperty("REAL").GetString());
        Assert.Equal(1, json.EnumerateObject().Count());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "DotEnvProvider")]
    public void Binds_through_ConfigManager_via_FromDotEnv()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "app.env"),
            """
            Name=myapp
            export Db__Port=5432
            Db:Host="localhost"
            """);

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<AppCfg>().FromDotEnv(Path.Combine(_dir.FullName, "app.env"))
            ]));

        var cfg = manager.GetConfig<AppCfg>()!;
        Assert.Equal("myapp", cfg.Name);
        Assert.Equal(5432, cfg.Db.Port);
        Assert.Equal("localhost", cfg.Db.Host);
    }

    public sealed class AppCfg
    {
        public string? Name { get; set; }
        public DbCfg Db { get; set; } = new();
    }

    public sealed class DbCfg
    {
        public int Port { get; set; }
        public string? Host { get; set; }
    }

    public void Dispose()
    {
        try { _dir.Delete(recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
