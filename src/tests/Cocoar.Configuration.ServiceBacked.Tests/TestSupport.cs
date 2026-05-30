using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Cocoar.Configuration.Providers; // IStorageBackend

namespace Cocoar.Configuration.ServiceBacked.Tests;

// ===== Shared config types =====

public sealed record LogConfig
{
    public string Level { get; init; } = "Info";
}

public sealed record RemoteConfig
{
    public string Value { get; init; } = "";
}

public sealed record TenantSettings
{
    public string Db { get; init; } = "";
}

// Two distinct types so two service-backed HTTP rules use two distinct providers/clients.
public sealed record ConfigA
{
    public string Value { get; init; } = "";
}

public sealed record ConfigB
{
    public string Value { get; init; } = "";
}

// ===== HTTP test double: a primary handler returning a fixed body, used to back an IHttpClientFactory client =====

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _respond;
    private int _callCount;

    public StubHttpHandler(string body)
        : this(_ => (HttpStatusCode.OK, body)) { }

    public StubHttpHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond)
    {
        _respond = respond;
    }

    public int CallCount => Volatile.Read(ref _callCount);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        var (status, body) = _respond(request);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

// ===== Storage test doubles =====

/// <summary>An in-memory <see cref="IStorageBackend"/> seeded with a fixed JSON body (read-only for tests).</summary>
internal sealed class SeededBackend : IStorageBackend
{
    private byte[]? _data;

    public SeededBackend(string json) => _data = Encoding.UTF8.GetBytes(json);

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(_data);

    public Task WriteAsync(string key, byte[] data, CancellationToken ct = default)
    {
        _data = data;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for a DI-managed document store (Marten <c>IDocumentStore</c>): a singleton that hands out a
/// per-tenant backend. Records the tenants it was asked for, so a test can prove the sp was actually used.
/// </summary>
internal interface IFakeDocumentStore
{
    IStorageBackend BackendFor(string? tenant);
    IReadOnlyCollection<string> RequestedTenants { get; }
}

internal sealed class FakeDocumentStore : IFakeDocumentStore
{
    private readonly ConcurrentDictionary<string, SeededBackend> _backends = new();
    private readonly ConcurrentDictionary<string, byte> _requested = new();

    public IStorageBackend BackendFor(string? tenant)
    {
        var key = tenant ?? "";
        _requested.TryAdd(key, 0);
        return _backends.GetOrAdd(key, t => new SeededBackend($$"""{ "Db": "db-for-{{t}}" }"""));
    }

    public IReadOnlyCollection<string> RequestedTenants => _requested.Keys.ToArray();
}

// ===== Reactive observer (BCL-only; avoids a System.Reactive test dependency) =====

internal sealed class CollectingObserver<T> : IObserver<T>
{
    private readonly List<T> _items = new();
    private readonly object _gate = new();

    public void OnNext(T value)
    {
        lock (_gate)
        {
            _items.Add(value);
        }
    }

    public void OnError(Exception error) { }

    public void OnCompleted() { }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }
}

// ===== Active-wait helper (no Thread.Sleep) =====

internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string description, int timeoutMs = 15000, int pollMs = 25)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch
            {
                // condition touched not-yet-ready state — treat as "not met yet"
            }

            await Task.Delay(pollMs);
        }

        throw new TimeoutException($"Timeout waiting for {description} after {timeoutMs}ms");
    }
}
