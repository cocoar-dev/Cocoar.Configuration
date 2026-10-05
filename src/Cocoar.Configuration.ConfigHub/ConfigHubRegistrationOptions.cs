namespace Cocoar.Configuration.ConfigHub;

/// <summary>Manager-wide registration behavior. Destinations and credentials come from active rules.</summary>
public sealed class ConfigHubRegistrationOptions
{
    /// <summary>Client name; defaults to the entry assembly's name.</summary>
    public string? ClientName { get; init; }
    /// <summary>Client release; defaults to the entry assembly's version.</summary>
    public string? ClientVersion { get; init; }
    /// <summary>Interval for retrying registration and discovering changed rules or server capabilities.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Timeout for each upstream request.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Optional caller-owned transport override. Otherwise uses the corresponding rule's handler.</summary>
    public HttpMessageHandler? Handler { get; init; }
}
