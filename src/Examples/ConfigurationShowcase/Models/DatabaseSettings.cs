using Cocoar.Configuration.Secrets.SecretTypes;

namespace ConfigurationShowcase.Models;

public interface IDatabaseSettings
{
    string ConnectionString { get; }
    int MaxPoolSize { get; }
    int CommandTimeoutSeconds { get; }
    bool EnableRetryOnFailure { get; }
}

public class DatabaseSettings : IDatabaseSettings
{
    public string ConnectionString { get; set; } = "";
    public int MaxPoolSize { get; set; } = 50;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnableRetryOnFailure { get; set; }
    public ISecret<string>? ApiKey { get; set; }
}
