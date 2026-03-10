using Microsoft.CodeAnalysis;

namespace Cocoar.Configuration.Flags.Generator;

internal static class FlagsGeneratorDiagnostics
{
    private const string Category = "CocoarFlags";

    /// <summary>
    /// Emitted when <c>ExpiresAt</c> is not a statically determinable <c>DateTimeOffset</c> literal.
    /// The class will be included but <c>ExpiresAt</c> will default to <c>DateTimeOffset.MinValue</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor NonStaticExpiresAt = new(
        id: "COCFLAG001",
        title: "Non-static ExpiresAt",
        messageFormat: "'{0}.ExpiresAt' could not be statically determined. The class will be registered with ExpiresAt = DateTimeOffset.MinValue (treated as expired). Use a DateTimeOffset literal: new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when a <c>DefineFlag</c> / <c>DefineEntitlement</c> call uses a non-literal <c>description</c>.
    /// The description will be omitted (null) in the generated descriptor.
    /// </summary>
    public static readonly DiagnosticDescriptor NonLiteralDescription = new(
        id: "COCFLAG002",
        title: "Non-literal description in DefineFlag/DefineEntitlement",
        messageFormat: "The 'description' argument in the call to '{0}' could not be statically determined. The description will be null in the generated descriptor.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when a <c>DefineFlag</c> call uses a non-literal <c>expiresAt</c> override.
    /// The flag will fall back to the class-level <c>ExpiresAt</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor NonStaticFlagExpiresAt = new(
        id: "COCFLAG003",
        title: "Non-static per-flag expiresAt",
        messageFormat: "The 'expiresAt' argument in the call to '{0}' could not be statically determined. The flag will use the class-level ExpiresAt.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
