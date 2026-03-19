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
    /// Emitted when <c>Register&lt;T&gt;()</c> is called with an abstract type.
    /// Abstract classes cannot be instantiated as flag/entitlement classes.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractTypeRegistered = new(
        id: "COCFLAG002",
        title: "Abstract type registered",
        messageFormat: "'{0}' is abstract and cannot be used with Register<T>(). Use a concrete subclass instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Emitted when a FeatureFlag or Entitlement property has no <c>&lt;summary&gt;</c> XML doc comment.
    /// Descriptions are surfaced through <c>IFeatureFlagsDescriptors</c> / <c>IEntitlementsDescriptors</c>
    /// and help operators understand what each flag controls.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingPropertyDescription = new(
        id: "COCFLAG003",
        title: "Missing flag/entitlement description",
        messageFormat: "Property '{0}' on '{1}' has no <summary> XML doc comment. Add a description so it appears in flag/entitlement descriptors.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
