using Cocoar.Configuration.Fluent.Providers;
using Cocoar.Configuration.Fluent.ProviderOptions;

namespace Cocoar.Configuration.Fluent;

public static class Rules
{
    public static FileRuleBuilder FromFile(Func<ConfigManager, FileSourceRuleOptions> optionsFactory) => new(optionsFactory);
    public static EnvironmentRuleBuilder FromEnvironment(Func<ConfigManager, EnvironmentVariableRuleOptions> optionsFactory) => new(optionsFactory);
    public static HttpRuleBuilder FromHttp(Func<ConfigManager, HttpPollingRuleOptions> optionsFactory) => new(optionsFactory);
}
