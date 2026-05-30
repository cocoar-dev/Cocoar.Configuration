using System.Text;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ADR-006 "service-backed" (Layer-2) configuration, end to end.
//
// Layer 1 (UseConfiguration) is eager and DI-free: a bootstrap default available before the container exists.
// Layer 2 (UseServiceBackedConfiguration) is lazy and container-owned: its FromStore factory resolves a
// DI-managed "store" (here an in-memory stand-in for Marten/EF) and overrides the base. Layer 2 activates on
// host start via a recompute — so a reactive view obtained BEFORE the host runs still receives the upgrade.

var builder = Host.CreateApplicationBuilder(args);

// A DI-managed singleton standing in for a real document store (Marten IDocumentStore / EF IDbContextFactory).
builder.Services.AddSingleton<IFeatureStore, InMemoryFeatureStore>();

builder.Services.AddCocoarConfiguration(c => c
    // Layer 1 — eager bootstrap default (no IServiceProvider).
    .UseConfiguration(rules =>
    [
        rules.For<FeatureConfig>().FromStaticJson("""{ "Banner": "bootstrap default", "MaxItems": 10 }"""),
    ])
    // Layer 2 — container-owned: the factory receives the IServiceProvider and resolves the store.
    .UseServiceBackedConfiguration(rules =>
    [
        rules.For<FeatureConfig>().FromStore((sp, _) => sp.GetRequiredService<IFeatureStore>().Backend),
    ])
    .UseDebounce(25));

using var host = builder.Build();
var config = host.Services.GetRequiredService<ConfigManager>();

// Subscribe BEFORE the host starts — like wiring a Serilog level switch during bootstrap.
var observer = new ConsoleObserver();
using var subscription = config.GetReactiveConfig<FeatureConfig>().Subscribe(observer);

Console.WriteLine("=== Before host start (Layer 1 only) ===");
Console.WriteLine($"  GetConfig: {config.GetConfig<FeatureConfig>()}");

Console.WriteLine("\n=== Starting host (activates Layer 2) ===");
await host.StartAsync();

Console.WriteLine("\n=== After host start (Layer 2 merged over Layer 1) ===");
Console.WriteLine($"  GetConfig: {config.GetConfig<FeatureConfig>()}");

await host.StopAsync();

Console.WriteLine("\nNote how the reactive subscription — obtained pre-container — received BOTH the");
Console.WriteLine("Layer-1 bootstrap value and the Layer-2 upgrade, on the same live view.");

// ----- types -----

public sealed record FeatureConfig
{
    public string Banner { get; init; } = "";
    public int MaxItems { get; init; }

    public override string ToString() => $"Banner='{Banner}', MaxItems={MaxItems}";
}

/// <summary>A DI-managed store; in a real app this wraps Marten's IDocumentStore or an EF IDbContextFactory.</summary>
public interface IFeatureStore
{
    IStoreBackend Backend { get; }
}

internal sealed class InMemoryFeatureStore : IFeatureStore
{
    // The "row" the store would load — overrides Banner, inherits MaxItems from the Layer-1 base (sparse overlay).
    public IStoreBackend Backend { get; } = new SeededBackend("""{ "Banner": "live from the database" }""");
}

internal sealed class SeededBackend(string json) : IStoreBackend
{
    private byte[] _data = Encoding.UTF8.GetBytes(json);

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult<byte[]?>(_data);

    public Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        _data = data;
        return Task.CompletedTask;
    }
}

internal sealed class ConsoleObserver : IObserver<FeatureConfig>
{
    public void OnNext(FeatureConfig value) => Console.WriteLine($"  [reactive] -> {value}");
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
