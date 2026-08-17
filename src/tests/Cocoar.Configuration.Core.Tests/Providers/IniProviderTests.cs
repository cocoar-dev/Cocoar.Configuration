using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.Core.Tests.Providers;

public class IniProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cocoar-ini-");

    private JsonElement Parse(string content, string filename = "config.ini")
    {
        File.WriteAllText(Path.Combine(_dir.FullName, filename), content);
        var provider = new IniProvider(new FileSourceProviderOptions(_dir.FullName));
        var bytes = provider.FetchConfigurationBytesAsync(new FileSourceProviderQueryOptions(filename))
            .GetAwaiter().GetResult();
        return JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "IniProvider")]
    public void Sections_nest_and_root_keys_stay_at_root()
    {
        var json = Parse(
            """
            app = myapp

            [db]
            host = localhost
            port = 5432
            """);

        Assert.Equal("myapp", json.GetProperty("app").GetString());
        Assert.Equal("localhost", json.GetProperty("db").GetProperty("host").GetString());
        Assert.Equal("5432", json.GetProperty("db").GetProperty("port").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "IniProvider")]
    public void Whole_line_comments_and_blanks_are_ignored()
    {
        var json = Parse(
            """
            ; semicolon comment
            # hash comment

            real = 1
            """);

        Assert.Equal("1", json.GetProperty("real").GetString());
        Assert.Equal(1, json.EnumerateObject().Count());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "IniProvider")]
    public void Nested_section_names_split_on_dot_and_colon()
    {
        var json = Parse(
            """
            [Db.Primary]
            host = a

            [Db:Replica]
            host = b
            """);

        Assert.Equal("a", json.GetProperty("Db").GetProperty("Primary").GetProperty("host").GetString());
        Assert.Equal("b", json.GetProperty("Db").GetProperty("Replica").GetProperty("host").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "IniProvider")]
    public void Quotes_are_stripped_and_inline_comment_chars_are_preserved()
    {
        var json = Parse(
            """
            [db]
            quoted = "hello world"
            conn = Server=db;Database=app;Trusted_Connection=true
            hash = a#b
            """);

        Assert.Equal("hello world", json.GetProperty("db").GetProperty("quoted").GetString());
        // A ';'/'#' inside a value must survive — no inline-comment stripping (connection-string safety).
        Assert.Equal("Server=db;Database=app;Trusted_Connection=true", json.GetProperty("db").GetProperty("conn").GetString());
        Assert.Equal("a#b", json.GetProperty("db").GetProperty("hash").GetString());
    }

    [Fact]
    [Trait("Type", "Unit")]
    [Trait("Provider", "IniProvider")]
    public void Binds_through_ConfigManager_via_FromIniFile()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "app.ini"),
            """
            Name = myapp

            [Db]
            Port = 5432
            Host = localhost
            """);

        using var manager = ConfigManager.Create(c => c
            .UseConfiguration(rules =>
            [
                rules.For<AppCfg>().FromIniFile(Path.Combine(_dir.FullName, "app.ini"))
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
