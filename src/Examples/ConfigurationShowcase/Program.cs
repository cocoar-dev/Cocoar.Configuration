using Cocoar.Configuration.AspNetCore;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Secrets;
using ConfigurationShowcase.Hubs;
using ConfigurationShowcase.Models;
using ConfigurationShowcase.Services;

var builder = WebApplication.CreateBuilder(args);

// Resolve config file paths relative to the app's content root
var configDir = Path.Combine(builder.Environment.ContentRootPath, "config");

builder.AddCocoarConfiguration(c => c
    .UseConfiguration(
        rules: rule =>
        [
            // --- File-based rules ---
            rule.For<AppSettings>()
                .FromFile(Path.Combine(configDir, "app.json"))
                .Select("AppSettings")
                .Named("AppSettings (file)")
                .Required(),

            // Environment overlay for AppSettings (prefix APP_)
            rule.For<AppSettings>()
                .FromEnvironment("APP_")
                .Named("AppSettings (env)"),

            rule.For<DatabaseSettings>()
                .FromFile(Path.Combine(configDir, "database.json"))
                .Named("DatabaseSettings")
                .Required(),

            // --- Static JSON rules ---
            rule.For<NotificationSettings>()
                .FromStaticJson("""
                {
                    "SmtpServer": "smtp.showcase.local",
                    "SmtpPort": 587,
                    "FromAddress": "showcase@cocoar.dev",
                    "EnableSlack": true,
                    "SlackWebhookUrl": "https://hooks.slack.example.com/showcase"
                }
                """)
                .Named("NotificationSettings (static)"),

            // --- Conditional rule: only loads when EnableDetailedErrors is true ---
            rule.For<DiagnosticsConfig>()
                .FromStaticJson("""
                {
                    "EnableRequestTracing": true,
                    "EnablePerformanceCounters": true,
                    "TraceRetentionMinutes": 30,
                    "TraceEndpoint": "/diagnostics/traces"
                }
                """)
                .Named("Diagnostics (conditional)")
                .When(accessor =>
                {
                    var app = accessor.GetConfig<AppSettings>();
                    return app.EnableDetailedErrors;
                })
        ],
        setup: setup =>
        [
            // ConcreteType + ExposeAs (2 interface exposures)
            setup.ConcreteType<AppSettings>().ExposeAs<IAppSettings>(),
            setup.ConcreteType<DatabaseSettings>().ExposeAs<IDatabaseSettings>(),

            // Interface deserialization pattern
            setup.Interface<INotificationSettings>().DeserializeTo<NotificationSettings>()
        ])
    .UseSecretsSetup(secrets => secrets.AllowPlaintext())
    .UseDebounce(500));

// Register tuple reactive config for the Reactive Demo page
var configManager = builder.GetCocoarConfigManager();
builder.Services.AddSingleton(configManager.GetReactiveConfig<(AppSettings App, DatabaseSettings Db)>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddHostedService<ConfigWatcherService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

// API endpoint for the ReactiveDemo file editor
app.MapPost("/api/config/app", async (HttpRequest request) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    var filePath = Path.Combine(configDir, "app.json");
    await File.WriteAllTextAsync(filePath, json);
    return Results.Ok(new { saved = true });
});

app.MapPost("/api/config/database", async (HttpRequest request) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    var filePath = Path.Combine(configDir, "database.json");
    await File.WriteAllTextAsync(filePath, json);
    return Results.Ok(new { saved = true });
});

app.MapGet("/api/config/{name}", (string name) =>
{
    var filePath = Path.Combine(configDir, $"{name}.json");
    if (!File.Exists(filePath)) return Results.NotFound();
    var content = File.ReadAllText(filePath);
    return Results.Text(content, "application/json");
});

app.MapHub<ConfigHub>("/confighub");

app.MapRazorComponents<ConfigurationShowcase.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
