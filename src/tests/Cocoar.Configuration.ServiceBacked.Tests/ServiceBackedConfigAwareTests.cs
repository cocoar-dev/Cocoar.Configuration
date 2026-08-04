using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.ServiceBacked.Tests;

/// <summary>
/// A Layer-2 factory receives <c>(IServiceProvider, IConfigurationAccessor)</c>, so it is config-aware in the
/// same sense as a Layer-1 factory: it must read the pass it runs in, both during the activation recompute and
/// on every later change to the Layer-1 value it depends on.
/// </summary>
[Trait("Category", "ServiceBacked")]
[Trait("Component", "ConfigAware")]
public class ServiceBackedConfigAwareTests
{
    public class SourceCfg { public string Name { get; set; } = "unset"; }

    public class RemoteCfg { public string Value { get; set; } = "unset"; }

    /// <remarks>
    /// Known failing, and deliberately narrower than it looks. Activation itself is correct — the factory reads
    /// the Layer-1 value and the store is seeded from it. What does not work is a LATER Layer-1 change: the
    /// factory runs again and returns a different backend, <c>WritableStoreRulesExtensions</c> detects that and
    /// calls <c>WritableStoreState.ReplaceBackend</c>, but that method only swaps the field. Nothing signals a
    /// change and nothing invalidates the rule's cache, so the store keeps serving what it read the first time.
    /// Either the swap should force a re-read or it should not happen at all. Fixing it touches the writable-store
    /// read/write model, so it is tracked separately rather than folded into the config-aware repair.
    /// </remarks>
    [Fact(Skip = "Known failing: a replaced writable-store backend never triggers a re-read. Remove Skip to reproduce.")]
    [Trait("Type", "Unit")]
    public async Task Layer2Factory_ReadsLayer1_AtActivationAndOnLaterChanges()
    {
        var source = new ReplayingSource("""{"Name":"one"}""");

        var services = new ServiceCollection();
        services.AddCocoarConfiguration(c => c
            .UseConfiguration(rules =>
            [
                rules.For<SourceCfg>().FromObservable(source),
                rules.For<RemoteCfg>().FromStaticJson("""{ "Value": "base" }"""),
            ])
            .UseServiceBackedConfiguration(rules =>
            [
                rules.For<RemoteCfg>().FromStore((_, accessor) =>
                    new SeededBackend($$"""{ "Value": "s-{{accessor.GetConfig<SourceCfg>()!.Name}}" }""")),
            ])
            .UseDebounce(25));

        await using var sp = services.BuildServiceProvider();
        var mgr = sp.GetRequiredService<ConfigManager>();

        // Dormant until activation: Layer 1 wins.
        Assert.Equal("base", mgr.GetConfig<RemoteCfg>()!.Value);

        await sp.ActivateServiceBackedConfigurationAsync();
        Assert.Equal("s-one", mgr.GetConfig<RemoteCfg>()!.Value);

        source.Push("""{"Name":"two"}""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && mgr.GetConfig<RemoteCfg>()!.Value != "s-two")
        {
            await Task.Delay(25);
        }

        Assert.Equal("two", mgr.GetConfig<SourceCfg>()!.Name);
        Assert.Equal("s-two", mgr.GetConfig<RemoteCfg>()!.Value);
    }

    /// <summary>
    /// Minimal replay-1 source. This project deliberately does not reference System.Reactive, and the test only
    /// needs "hand the current value to a new subscriber, then push updates".
    /// </summary>
    private sealed class ReplayingSource(string initial) : IObservable<string>
    {
        private readonly List<IObserver<string>> _observers = [];
        private string _current = initial;

        public IDisposable Subscribe(IObserver<string> observer)
        {
            string current;
            lock (_observers)
            {
                _observers.Add(observer);
                current = _current;
            }

            observer.OnNext(current);
            return new Subscription(this, observer);
        }

        public void Push(string value)
        {
            IObserver<string>[] snapshot;
            lock (_observers)
            {
                _current = value;
                snapshot = [.. _observers];
            }

            foreach (var observer in snapshot)
            {
                observer.OnNext(value);
            }
        }

        private sealed class Subscription(ReplayingSource source, IObserver<string> observer) : IDisposable
        {
            public void Dispose()
            {
                lock (source._observers)
                {
                    source._observers.Remove(observer);
                }
            }
        }
    }
}
