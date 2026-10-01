using System.Net;
using System.Net.Http.Headers;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers;

namespace Cocoar.Configuration.ConfigHub.Tests;

public sealed class DynamicFromConfigHubTests
{
    [Fact]
    public async Task DynamicRuleSwitchesSnapshotUrlWhenItsSourceChanges()
    {
        using var source = new BehaviorSource<string>("""{ "Region": "us" }""");
        using var handler = new RegionRoutingHandler();

        using var manager = ConfigManager.Create(configuration => configuration
            .UseConfiguration(rules =>
            [
                rules.For<RegionSettings>().FromObservable(source),
                rules.For<RemoteSettings>().FromConfigHub(accessor => new ConfigHubRuleOptions(
                    $"https://config.example/{accessor.GetConfig<RegionSettings>()!.Region}/config",
                    "chcfg_dynamic-test",
                    handler: handler)),
            ])
            .UseDebounce(25));

        Assert.Equal("US", manager.GetConfig<RemoteSettings>()!.Value);

        source.OnNext("""{ "Region": "eu" }""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && manager.GetConfig<RemoteSettings>()!.Value != "EU")
        {
            await Task.Delay(25, CancellationToken.None);
        }

        Assert.Equal("eu", manager.GetConfig<RegionSettings>()!.Region);
        Assert.Equal("EU", manager.GetConfig<RemoteSettings>()!.Value);
        Assert.Contains(handler.SnapshotPaths, path => path.Contains("/eu/", StringComparison.Ordinal));
    }

    private sealed class RegionSettings
    {
        public string Region { get; set; } = string.Empty;
    }

    private sealed class RemoteSettings
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class RegionRoutingHandler : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string> _snapshotPaths = [];

        public IReadOnlyList<string> SnapshotPaths
        {
            get
            {
                lock (_gate)
                {
                    return _snapshotPaths.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Accept.Any(value => value.MediaType == "text/event-stream"))
            {
                var stream = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(": connected\n\n"),
                };
                stream.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(stream);
            }

            var path = request.RequestUri!.AbsolutePath;
            lock (_gate)
            {
                _snapshotPaths.Add(path);
            }

            var value = path.Contains("/eu/", StringComparison.Ordinal) ? "EU" : "US";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{ "Value": "{{value}}" }"""),
            });
        }
    }

    private sealed class BehaviorSource<T>(T initialValue) : IObservable<T>, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<IObserver<T>> _observers = [];
        private T _value = initialValue;
        private bool _disposed;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _observers.Add(observer);
                observer.OnNext(_value);
            }

            return new ObserverSubscription(this, observer);
        }

        public void OnNext(T value)
        {
            IObserver<T>[] observers;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _value = value;
                observers = _observers.ToArray();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(value);
            }
        }

        public void Dispose()
        {
            IObserver<T>[] observers;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                observers = _observers.ToArray();
                _observers.Clear();
            }

            foreach (var observer in observers)
            {
                observer.OnCompleted();
            }
        }

        private void Unsubscribe(IObserver<T> observer)
        {
            lock (_gate)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class ObserverSubscription(
            BehaviorSource<T> source,
            IObserver<T> observer) : IDisposable
        {
            public void Dispose() => source.Unsubscribe(observer);
        }
    }
}
