using Cocoar.Configuration.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cocoar.Configuration.AspNetCore.Health;

/// <summary>
/// ASP.NET Core <see cref="IHealthCheck"/> that maps <see cref="Configuration.Health.HealthStatus"/>
/// to <see cref="HealthCheckResult"/>.
/// </summary>
internal sealed class CocoarConfigurationHealthCheck : IHealthCheck
{
    private readonly ConfigManager _configManager;

    public CocoarConfigurationHealthCheck(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = _configManager.HealthStatus switch
        {
            Configuration.Health.HealthStatus.Healthy => HealthCheckResult.Healthy("All rules healthy"),
            Configuration.Health.HealthStatus.Degraded => HealthCheckResult.Degraded(_configManager.HealthDescription),
            Configuration.Health.HealthStatus.Unhealthy => HealthCheckResult.Unhealthy(_configManager.HealthDescription),
            _ => HealthCheckResult.Degraded("Health status unknown")
        };

        return Task.FromResult(result);
    }
}
