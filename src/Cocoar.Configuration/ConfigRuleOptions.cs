namespace Cocoar.Configuration;

public sealed record ConfigRuleOptions(
    bool Required = true,
    Func<bool>? UseWhen = null,
    string? MountPath = null)
{
    public ConfigRuleOptions WithMount(string? mountPath)
        => this with { MountPath = string.IsNullOrWhiteSpace(mountPath) ? null : mountPath!.Trim() };
}
