using Cocoar.Configuration.Secrets.SecretTypes;
using Serilog.Events;

namespace ShowCase;
public class StartUpConfiguration
{
    public string AppUrl { get; set; } = "https://0.0.0.0:443";
    
    public string? CertPath { get; set; } = "localhost.pfx";

    public string? CertPassword { get; set; } = "ABC12abc";
    
    public Logging Logging { get; set; } = new Logging();

    public DatabaseConfiguration DbSettings { get; set; } = new();

    public Secret<byte[]> MySecret { get; set; } = null!;

    public Secret<NetworkCredentialConfig> Credentials { get; set; } = null!;
}

public class Logging
{
    public string LogPath { get; set; } = "";

    private Dictionary<string, LogEventLevel?>? _loglevel;

    public Dictionary<string, LogEventLevel?> LogLevel
    {
        get => _loglevel ??= GetDefaultLoggings();
        set => _loglevel = value;
    }



    internal static Dictionary<string, LogEventLevel?> GetDefaultLoggings()
    {
        return new Dictionary<string, LogEventLevel?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = LogEventLevel.Warning,
            ["Microsoft.Hosting.Lifetime"] = LogEventLevel.Information,
        };
    }
}

public class DatabaseConfiguration
{
    public string ConnectionString { get; set; } = "Host=127.0.0.1:5432;Database=timetodo;Username=postgres;Password=postgres";
}
