using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.AspNetCore.Health;

public static class CocoarHealthCheckExtensions
{
    /// <summary>
    /// Adds the Cocoar configuration health check to the health check builder.
    /// Maps <see cref="Cocoar.Configuration.Health.HealthStatus"/> to ASP.NET Core
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult"/>.
    /// </summary>
    public static IHealthChecksBuilder AddCocoarConfigurationHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "cocoar-configuration",
        params string[] tags)
    {
        builder.AddCheck<CocoarConfigurationHealthCheck>(name, tags: tags);
        return builder;
    }
}
