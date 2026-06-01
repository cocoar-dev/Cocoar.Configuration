namespace Cocoar.Configuration.Marten;

/// <summary>
/// The Marten document that persists one WritableStore overlay. There is one document per configuration type:
/// <see cref="Id"/> is the storage key (the configuration type's full name) and <see cref="Json"/> is the sparse
/// overlay JSON the WritableStore reads and writes.
/// </summary>
/// <remarks>
/// <para>
/// You normally never touch this type directly — <see cref="MartenStoreBackend"/> stores and loads it. It is public
/// so you can register it with Marten to control schema creation, e.g. <c>options.Schema.For&lt;CocoarConfigDocument&gt;()</c>,
/// rather than relying on Marten's runtime auto-creation.
/// </para>
/// <para>
/// With Marten database-per-tenant the document lives in the tenant's own database: the backend opens the session
/// for the current <c>accessor.Tenant</c>, so each tenant's configuration overlay is isolated by database.
/// </para>
/// </remarks>
public sealed class CocoarConfigDocument
{
    /// <summary>The storage key — the configuration type's full name (e.g. <c>MyApp.Configuration.SmtpSettings</c>).</summary>
    public string Id { get; set; } = default!;

    /// <summary>The sparse overlay JSON (UTF-8 text) for this configuration type. Defaults to an empty object.</summary>
    public string Json { get; set; } = "{}";
}
