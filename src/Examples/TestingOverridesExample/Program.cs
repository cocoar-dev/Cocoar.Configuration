using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Providers;

namespace Examples.TestingOverridesExample;

public class DbConfig
{
    public string ConnectionString { get; set; } = "";
    public int MaxConnections { get; set; } = 10;
}

public class ApiSettings
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Regular configuration - these rules would normally run
        builder.AddCocoarConfiguration(rule => [
            rule.For<DbConfig>().FromFile("config.json").Select("Database"),
            rule.For<ApiSettings>().FromFile("config.json").Select("Api")
        ]);

        var app = builder.Build();

        app.MapGet("/config", (DbConfig db, ApiSettings api) => new
        {
            Database = new { db.ConnectionString, db.MaxConnections },
            Api = new { api.BaseUrl, api.ApiKey }
        });

        app.Run();
    }
}
