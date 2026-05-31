using Cocoar.Configuration.Providers.Abstractions;
using Xunit;

namespace Cocoar.Configuration.Providers.Tests;

/// <summary>
/// Verifies the public provider-authoring reactive helpers (<see cref="ProviderObservable"/> /
/// <see cref="ProviderDisposable"/>) behave as documented for a custom provider's
/// <c>ChangesAsBytes</c> stream — without referencing System.Reactive.
/// </summary>
public sealed class ProviderReactiveTests
{
    private sealed class RecordingObserver<T> : IObserver<T>
    {
        public List<T> Values { get; } = new();
        public bool Completed { get; private set; }
        public Exception? Error { get; private set; }
        public void OnNext(T value) => Values.Add(value);
        public void OnCompleted() => Completed = true;
        public void OnError(Exception error) => Error = error;
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Never_DoesNotEmitOrComplete()
    {
        var observer = new RecordingObserver<byte[]>();

        using var subscription = ProviderObservable.Never<byte[]>().Subscribe(observer);

        Assert.Empty(observer.Values);
        Assert.False(observer.Completed);
        Assert.Null(observer.Error);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Empty_CompletesWithoutEmitting()
    {
        var observer = new RecordingObserver<byte[]>();

        ProviderObservable.Empty<byte[]>().Subscribe(observer);

        Assert.Empty(observer.Values);
        Assert.True(observer.Completed);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Create_WiresSubscribeCallback_AndReturnsTeardown()
    {
        var observer = new RecordingObserver<int>();
        var torn = false;

        var observable = ProviderObservable.Create<int>(o =>
        {
            o.OnNext(1);
            o.OnNext(2);
            return ProviderDisposable.Create(() => torn = true);
        });

        var subscription = observable.Subscribe(observer);
        Assert.Equal(new[] { 1, 2 }, observer.Values);
        Assert.False(torn);

        subscription.Dispose();
        Assert.True(torn);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Disposable_Create_RunsActionExactlyOnce()
    {
        var count = 0;
        var disposable = ProviderDisposable.Create(() => count++);

        disposable.Dispose();
        disposable.Dispose();

        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Type", "Unit")]
    public void Disposable_Empty_IsNoOp()
    {
        var disposable = ProviderDisposable.Empty;

        disposable.Dispose(); // must not throw

        Assert.NotNull(disposable);
    }
}
