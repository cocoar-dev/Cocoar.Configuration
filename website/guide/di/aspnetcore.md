# ASP.NET Core Integration

The `Cocoar.Configuration.AspNetCore` package adds ASP.NET Core-specific features on top of the DI package: health endpoints, feature flag endpoints, and `WebApplicationBuilder` extensions.

::: info Package
`Cocoar.Configuration.AspNetCore` includes `Cocoar.Configuration.DI` transitively — you only need one package reference.
:::

## WebApplicationBuilder Extension

Instead of `builder.Services.AddCocoarConfiguration(...)`, you can call it directly on the builder:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddCocoarConfiguration(c => c
    .UseConfiguration(rules => [
        rules.For<AppSettings>().FromFile("appsettings.json")
    ])
    .UseFeatureFlags(flags => [flags.Register<AppFlags>()]));
```

Both forms are equivalent. The `WebApplicationBuilder` extension delegates to `builder.Services.AddCocoarConfiguration(...)`.

## Health Endpoint

See [ASP.NET Core Health Checks](/guide/health/aspnetcore) for setup.

```csharp
builder.Services
    .AddHealthChecks()
    .AddCocoarConfigurationHealthCheck();

var app = builder.Build();
app.MapHealthChecks("/health");
```

## Feature Flag & Entitlement Endpoints

See [REST Evaluation Endpoints](/guide/flags/rest-endpoints) for details.

```csharp
app.MapFeatureFlagEndpoints();      // → /flags/{Class}/{Property}
app.MapEntitlementEndpoints();      // → /entitlements/{Class}/{Property}
```

Both return a `RouteGroupBuilder` for chaining middleware:

```csharp
app.MapFeatureFlagEndpoints()
    .RequireAuthorization("AdminPolicy")
    .RequireRateLimiting("fixed");
```

## Full Example

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register Cocoar configuration
builder.AddCocoarConfiguration(c => c
    .UseConfiguration(
        rules => [
            rules.For<AppSettings>().FromFile("appsettings.json").Required(),
            rules.For<FeatureConfig>().FromFile("features.json")
        ])
    .UseFeatureFlags(
        flags => [flags.Register<AppFlags>()],
        resolvers => [resolvers.Global<UserByIdResolver>()])
    .UseEntitlements(
        entitlements => [entitlements.Register<PlanEntitlements>()]));

// Health checks
builder.Services
    .AddHealthChecks()
    .AddCocoarConfigurationHealthCheck();

var app = builder.Build();

// Endpoints
app.MapHealthChecks("/health");
app.MapFeatureFlagEndpoints().RequireAuthorization();
app.MapEntitlementEndpoints().RequireAuthorization();

app.Run();
```

## Injecting Configuration

Once registered, inject configuration types as you would any other service:

```csharp
public class OrderService(AppSettings settings, IReactiveConfig<AppSettings> reactive)
{
    public void Process()
    {
        // Scoped snapshot — stable for this request
        var maxRetries = settings.MaxRetries;

        // Live subscription — for background work
        reactive.Subscribe(updated => Console.WriteLine($"Config changed: {updated.MaxRetries}"));
    }
}
```

Feature flags and entitlements are injected the same way:

```csharp
public class CheckoutController(BillingFlags flags, PlanEntitlements entitlements)
{
    public IActionResult Index()
    {
        if (flags.NewDashboard())
            return View("NewDashboard");

        var maxUsers = entitlements.MaxUsers();
        // ...
    }
}
```
