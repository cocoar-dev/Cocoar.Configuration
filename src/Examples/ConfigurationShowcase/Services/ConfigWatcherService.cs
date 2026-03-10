using System.Reactive.Linq;
using Cocoar.Configuration.Health;
using Cocoar.Configuration.Reactive;
using ConfigurationShowcase.Hubs;
using ConfigurationShowcase.Models;
using Microsoft.AspNetCore.SignalR;

namespace ConfigurationShowcase.Services;

/// <summary>
/// Background service that subscribes to reactive config and health streams,
/// then pushes updates to the SignalR ConfigHub for external consumers.
/// </summary>
public sealed class ConfigWatcherService : BackgroundService
{
    private readonly IHubContext<ConfigHub> _hubContext;
    private readonly IReactiveConfig<AppSettings> _appConfig;
    private readonly IReactiveConfig<DatabaseSettings> _dbConfig;
    private readonly IConfigurationHealthService _healthService;

    public ConfigWatcherService(
        IHubContext<ConfigHub> hubContext,
        IReactiveConfig<AppSettings> appConfig,
        IReactiveConfig<DatabaseSettings> dbConfig,
        IConfigurationHealthService healthService)
    {
        _hubContext = hubContext;
        _appConfig = appConfig;
        _dbConfig = dbConfig;
        _healthService = healthService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var appSub = _appConfig.Subscribe(config =>
            _hubContext.Clients.All.SendAsync("AppSettingsChanged", new
            {
                config.ApplicationName,
                config.Version,
                config.MaxRetries,
                config.EnableDetailedErrors,
                config.WelcomeMessage,
                Timestamp = DateTime.UtcNow
            }, stoppingToken));

        var dbSub = _dbConfig.Subscribe(config =>
            _hubContext.Clients.All.SendAsync("DatabaseSettingsChanged", new
            {
                config.ConnectionString,
                config.MaxPoolSize,
                config.CommandTimeoutSeconds,
                config.EnableRetryOnFailure,
                Timestamp = DateTime.UtcNow
            }, stoppingToken));

        var healthSub = _healthService.SnapshotStream.Subscribe(snapshot =>
            _hubContext.Clients.All.SendAsync("HealthUpdated", new
            {
                snapshot.OverallStatus,
                snapshot.ConfigVersion,
                snapshot.TimestampUtc,
                RuleCount = snapshot.Rules.Count,
                Summary = new
                {
                    snapshot.Summary.Total,
                    snapshot.Summary.RequiredFailed,
                    snapshot.Summary.OptionalFailed,
                    snapshot.Summary.Skipped
                }
            }, stoppingToken));

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        finally
        {
            appSub.Dispose();
            dbSub.Dispose();
            healthSub.Dispose();
        }
    }
}
