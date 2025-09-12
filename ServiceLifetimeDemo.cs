using Cocoar.Configuration;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.StaticJsonProvider;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== Cocoar Configuration Service Lifetime Demo ===\n");

// Define test configuration interfaces and classes
public interface IMyService
{
    string Name { get; }
    int Value { get; }
    string InstanceId { get; }
}

public class MyServiceConfig : IMyService
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public string InstanceId { get; } = Guid.NewGuid().ToString()[..8];
}

// Example 1: Multiple lifetimes with keys
Console.WriteLine("1. Multiple lifetimes with keys:");
var multiLifetimeRules = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "MultiLifetime", Value = 100 })
    .For<MyServiceConfig>()
    .AsSingleton<IMyService>("singleton-key")
    .AsScoped<IMyService>("scoped-key")
    .AsTransient<IMyService>("transient-key")
    .BuildRules()
    .ToList();

Console.WriteLine($"Created {multiLifetimeRules.Count} rules:");
foreach (var rule in multiLifetimeRules)
{
    Console.WriteLine($"  - {rule.Registration.ServiceLifetime} with key '{rule.Registration.ServiceKey}'");
}

var services = new ServiceCollection();
services.AddCocoarConfiguration(multiLifetimeRules);
var provider = services.BuildServiceProvider();

// Test singleton behavior
var singleton1 = provider.GetRequiredKeyedService<IMyService>("singleton-key");
var singleton2 = provider.GetRequiredKeyedService<IMyService>("singleton-key");
Console.WriteLine($"\nSingleton instances: {singleton1.InstanceId} == {singleton2.InstanceId} ? {singleton1.InstanceId == singleton2.InstanceId}");

// Test scoped behavior
using (var scope = provider.CreateScope())
{
    var scoped1 = scope.ServiceProvider.GetRequiredKeyedService<IMyService>("scoped-key");
    var scoped2 = scope.ServiceProvider.GetRequiredKeyedService<IMyService>("scoped-key");
    Console.WriteLine($"Scoped instances (same scope): {scoped1.InstanceId} == {scoped2.InstanceId} ? {scoped1.InstanceId == scoped2.InstanceId}");
}

// Test transient behavior
var transient1 = provider.GetRequiredKeyedService<IMyService>("transient-key");
var transient2 = provider.GetRequiredKeyedService<IMyService>("transient-key");
Console.WriteLine($"Transient instances: {transient1.InstanceId} == {transient2.InstanceId} ? {transient1.InstanceId == transient2.InstanceId}");

// Example 2: Backward compatibility (implicit singleton)
Console.WriteLine("\n2. Backward compatibility (implicit singleton):");
var legacyRule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Legacy", Value = 200 })
    .For<MyServiceConfig>()
    .Build(); // No explicit lifetime - defaults to singleton

Console.WriteLine($"Legacy rule lifetime: {legacyRule.Registration.ServiceLifetime}");

var legacyServices = new ServiceCollection();
legacyServices.AddCocoarConfiguration(legacyRule);
var legacyProvider = legacyServices.BuildServiceProvider();

var legacy1 = legacyProvider.GetRequiredService<MyServiceConfig>();
var legacy2 = legacyProvider.GetRequiredService<MyServiceConfig>();
Console.WriteLine($"Legacy instances: {legacy1.InstanceId} == {legacy2.InstanceId} ? {legacy1.InstanceId == legacy2.InstanceId}");

// Example 3: Mixed single and multiple registrations
Console.WriteLine("\n3. Mixed single and multiple registrations:");
var singleRule = Rule.From.Static<MyServiceConfig>(_ => new MyServiceConfig { Name = "Single", Value = 300 })
    .For<MyServiceConfig>()
    .AsScoped<IMyService>()
    .Build();

Console.WriteLine($"Single rule lifetime: {singleRule.Registration.ServiceLifetime}");

Console.WriteLine("\n=== Demo Complete ===");
