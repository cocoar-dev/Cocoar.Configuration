namespace Cocoar.Configuration.Rules;

public sealed record ConfigRuleOptions(
    bool Required = false,
    Func<bool>? UseWhen = null,
    string? MountPath = null,
    string? SelectPath = null)
{
    public ConfigRuleOptions WithMount(string? mountPath)
        => this with { MountPath = string.IsNullOrWhiteSpace(mountPath) ? null : mountPath.Trim() };

    public ConfigRuleOptions WithSelect(string? selectPath)
        => this with { SelectPath = Normalize(selectPath) };

    static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return string.Join(':', path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
