namespace Cocoar.Configuration;

public class ConfigRuleOptions
{
    public Func<bool>? UseWhen { get; set; }
    public bool Required { get; set; }
}
