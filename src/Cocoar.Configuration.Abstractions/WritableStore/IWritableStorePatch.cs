using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Cocoar.Configuration.Secrets.SecretTypes;

namespace Cocoar.Configuration.WritableStore;

/// <summary>
/// Fluent builder for a batched, atomic store write. Collect one or more mutations via
/// <see cref="Set"/>, <see cref="SetSecret"/>, or <see cref="Reset"/>; they are all applied in call order,
/// in one lock acquisition — one write to storage, one recompute — when the <c>PatchAsync</c> callback returns.
/// There is no separate commit step.
/// <para>
/// Semantics: calling <see cref="Set"/> sets the value (including an explicit <see langword="null"/>); not
/// calling it leaves the property untouched; <see cref="Reset"/> removes the override entirely. Mapping any
/// external input (HTTP body, an <c>Optional&lt;T&gt;</c> DTO, …) onto these calls is the caller's concern.
/// </para>
/// </summary>
/// <typeparam name="T">The configuration type this patch targets.</typeparam>
public interface IWritableStorePatch<T> where T : class
{
    /// <summary>Sets a single non-secret property.</summary>
    /// <exception cref="NotSupportedException">The selector targets a secret-typed member or contains one — use <see cref="SetSecret"/>.</exception>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "'Set' is the established fluent setter verb for this problem (EF Core SetProperty, MongoDB/Marten Set); renaming hurts the common path.")]
    IWritableStorePatch<T> Set<TValue>(Expression<Func<T, TValue>> selector, TValue value);

    /// <summary>Sets a pre-encrypted secret envelope for a secret-typed member. Mirrors <c>IWritableStore&lt;T&gt;.SetSecretAsync</c>.</summary>
    IWritableStorePatch<T> SetSecret<TSecret>(Expression<Func<T, ISecret<TSecret>>> selector, SecretEnvelope<TSecret> envelope);

    /// <summary>Removes the override for a single property (secret members included), restoring inheritance from lower layers.</summary>
    IWritableStorePatch<T> Reset<TValue>(Expression<Func<T, TValue>> selector);
}
