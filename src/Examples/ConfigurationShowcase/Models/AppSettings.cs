namespace ConfigurationShowcase.Models;

public interface IAppSettings
{
    string ApplicationName { get; }
    string Version { get; }
    int MaxRetries { get; }
    bool EnableDetailedErrors { get; }
    string WelcomeMessage { get; }
}

public class AppSettings : IAppSettings
{
    public string ApplicationName { get; set; } = "Cocoar Showcase";
    public string Version { get; set; } = "1.0.0";
    public int MaxRetries { get; set; } = 3;
    public bool EnableDetailedErrors { get; set; }
    public string WelcomeMessage { get; set; } = "";
}
