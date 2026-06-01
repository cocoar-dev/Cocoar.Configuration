using System.Linq.Expressions;
using Cocoar.Configuration.Secrets.SecretTypes;
using Cocoar.Configuration.WritableStore;

namespace Cocoar.Configuration.Providers;

/// <summary>
/// Implements <see cref="IWritableStorePatch{T}"/> — collects typed and raw mutations for a single
/// atomic <c>PatchAsync</c> commit. Not thread-safe; one instance per <c>PatchAsync</c> call.
/// </summary>
internal sealed class StorePatchBuilder<T> : IWritableStorePatch<T> where T : class
{
    // One ordered list of steps (typed sets/resets and JSON applies). Resolved in call order so that
    // last-write-wins reflects the order the caller actually wrote — including across Set and ApplyJson.
    internal readonly List<StorePatchMutation> Mutations = [];

    public IWritableStorePatch<T> Set<TValue>(Expression<Func<T, TValue>> selector, TValue value)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (OverlayPathResolver.ContainsSecret(typeof(TValue)))
            throw new NotSupportedException(
                $"Cannot store a value of type '{typeof(TValue).Name}' because it is, or contains, a secret. " +
                "Use SetSecret with a pre-encrypted SecretEnvelope for secret members.");
        Mutations.Add(new TypedSetMutation(selector, value, typeof(TValue)));
        return this;
    }

    public IWritableStorePatch<T> SetSecret<TSecret>(Expression<Func<T, ISecret<TSecret>>> selector, SecretEnvelope<TSecret> envelope)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(envelope);
        Mutations.Add(new TypedSecretMutation(selector, envelope));
        return this;
    }

    public IWritableStorePatch<T> Reset<TValue>(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Mutations.Add(new TypedResetMutation(selector));
        return this;
    }
}

internal abstract class StorePatchMutation { }

internal sealed class TypedSetMutation(LambdaExpression selector, object? value, Type valueType) : StorePatchMutation
{
    internal LambdaExpression Selector => selector;
    internal object? Value => value;
    internal Type ValueType => valueType;
}

internal sealed class TypedSecretMutation(LambdaExpression selector, object envelope) : StorePatchMutation
{
    internal LambdaExpression Selector => selector;
    internal object Envelope => envelope;
}

internal sealed class TypedResetMutation(LambdaExpression selector) : StorePatchMutation
{
    internal LambdaExpression Selector => selector;
}
