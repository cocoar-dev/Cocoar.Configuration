using Microsoft.CodeAnalysis;

namespace Cocoar.Configuration.Analyzers;

/// <summary>
/// Diagnostic descriptors for Cocoar.Configuration analyzers.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Cocoar.Configuration";

    /// <summary>
    /// CA001: Secret path conflict detected.
    /// A non-secret property has the same path as a secret property, risking plaintext exposure.
    /// </summary>
    public static readonly DiagnosticDescriptor SecretPathConflict = new(
        id: "COCFG001",
        title: "Secret path conflict detected",
        messageFormat: "Property '{0}' conflicts with secret property '{1}'. Consider using Secret<T> or renaming to avoid plaintext exposure.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A configuration property has the same path as a property marked with Secret<T>, which may cause sensitive data to be exposed as plaintext.",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG001.md");

    /// <summary>
    /// CA002: Rule dependency ordering violation.
    /// A rule depends on configuration that hasn't been loaded yet.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleOrderingViolation = new(
        id: "COCFG002",
        title: "Rule dependency ordering violation",
        messageFormat: "Rule for '{0}' depends on '{1}' which is not available yet. Move this rule after the '{1}' rule.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A configuration rule uses GetRequiredConfig<T> for a type that hasn't been loaded yet, which will cause a runtime exception.",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG002.md");

    /// <summary>
    /// CA003: Required rule validation.
    /// A required rule references a file or resource that may not exist.
    /// </summary>
    public static readonly DiagnosticDescriptor RequiredRuleValidation = new(
        id: "COCFG003",
        title: "Required rule configuration validation",
        messageFormat: "Required rule for '{0}' references '{1}' which may not exist. Application will fail to start if this resource is missing.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A required configuration rule references a file or resource that doesn't exist in the project, which will cause startup failure.",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG003.md");

    /// <summary>
    /// CA004: Type safety violation in configuration accessor.
    /// GetRequiredConfig is called with a type that doesn't have a matching property.
    /// </summary>
    public static readonly DiagnosticDescriptor ConfigurationAccessorTypeSafety = new(
        id: "COCFG004",
        title: "Configuration accessor type safety violation",
        messageFormat: "Property '{0}' does not exist on type '{1}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A configuration accessor attempts to access a property that doesn't exist on the specified type.",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG004.md");

    /// <summary>
    /// CA005: Duplicate unconditional rules for same type.
    /// Multiple rules configure the same type without conditions (last write wins).
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateUnconditionalRules = new(
        id: "COCFG005",
        title: "Duplicate unconditional rules detected",
        messageFormat: "Multiple unconditional rules for type '{0}'. Last rule will override earlier rules. Consider using .When() conditions or removing duplicates.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Multiple configuration rules target the same type without conditions. Only the last rule will be effective (last write wins).",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG005.md");

    /// <summary>
    /// CA006: Static provider ordering suggestion.
    /// Static/seed rules should generally appear before dynamic rules that may depend on them.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticProviderOrdering = new(
        id: "COCFG006",
        title: "Static provider ordering suggestion",
        messageFormat: "Static/seed rule found after dynamic rules. Consider moving static rules first to ensure they're available to dynamic rules.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Static or seed rules (FromStatic, FromObservable) should generally appear before dynamic rules (FromFile, FromHttpPolling) that may depend on their configuration.",
        helpLinkUri: "https://github.com/cocoar-dev/cocoar.configuration/blob/develop/docs/analyzers/COCFG006.md");
}
