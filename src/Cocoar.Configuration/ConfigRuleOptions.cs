namespace Cocoar.Configuration;

public sealed record ConfigRuleOptions(
    bool Required = true,
    Func<bool>? UseWhen = null,
    string? MountPath = null,
    string? SelectPath = null)
{
    public ConfigRuleOptions WithMount(string? mountPath)
        => this with { MountPath = string.IsNullOrWhiteSpace(mountPath) ? null : mountPath!.Trim() };

    public ConfigRuleOptions WithSelect(string? selectPath)
        => this with { SelectPath = Normalize(selectPath) };

    static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Accept colon-delimited segments (e.g., Section:Sub:Leaf). Trim whitespace.
        return string.Join(':', path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
