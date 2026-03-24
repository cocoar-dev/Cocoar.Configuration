using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cocoar.Configuration.Analyzers.Analyzers;

/// <summary>
/// Analyzer that validates static provider ordering.
/// Suggests placing static/seed rules before dynamic rules that may depend on them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StaticProviderOrderingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.StaticProviderOrdering);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!IsAddCocoarConfigurationCall(invocation, context.SemanticModel))
        {
            return;
        }

        // Extract rules from lambda
        var lambdas = invocation.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>();
        foreach (var lambda in lambdas)
        {
            AnalyzeProviderOrdering(lambda, context);
        }
    }

    private static bool IsAddCocoarConfigurationCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null)
        {
            return false;
        }

        var methodSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
        return methodSymbol?.Name == "AddCocoarConfiguration";
    }

    private static void AnalyzeProviderOrdering(SimpleLambdaExpressionSyntax lambda, SyntaxNodeAnalysisContext context)
    {
        var rules = new List<(bool isStatic, Location location)>();

        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            var providerInfo = ExtractProviderInfo(inv, context.SemanticModel);
            if (providerInfo.HasValue)
            {
                rules.Add(providerInfo.Value);
            }
        }

        // Check if dynamic rules come before static rules (suboptimal ordering)
        bool foundDynamic = false;
        Location? firstDynamicLocation = null;

        foreach (var (isStatic, location) in rules)
        {
            if (!isStatic)
            {
                foundDynamic = true;
                firstDynamicLocation ??= location;
            }
            else if (foundDynamic)
            {
                // Found static rule after dynamic rule - suggest reordering
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.StaticProviderOrdering,
                    location);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static (bool isStatic, Location location)? ExtractProviderInfo(
        InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Walk the invocation chain to find the provider method — the first method
        // called on TypedRuleBuilder<T> (returned by rule.For<T>())
        var parent = invocation;
        while (parent != null)
        {
            if (parent.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
                if (symbol != null && IsExtensionOnTypedRuleBuilder(symbol))
                {
                    var methodName = symbol.Name;
                    bool isStatic = methodName.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0;
                    return (isStatic, parent.GetLocation());
                }
            }
            parent = parent.Parent as InvocationExpressionSyntax;
        }

        return null;
    }

    /// <summary>
    /// Checks if the method is an extension method whose first parameter is
    /// TypedRuleBuilder&lt;T&gt; or TypedProviderBuilder&lt;T&gt;.
    /// This is the architectural definition of a provider method — no naming convention needed.
    /// </summary>
    private static bool IsExtensionOnTypedRuleBuilder(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod) return false;

        var receiverType = method.ReducedFrom?.Parameters.FirstOrDefault()?.Type
                           ?? method.Parameters.FirstOrDefault()?.Type;
        if (receiverType is not INamedTypeSymbol namedType) return false;

        return (namedType.Name == "TypedRuleBuilder" || namedType.Name == "TypedProviderBuilder")
               && namedType.ContainingNamespace?.ToString() == "Cocoar.Configuration.Fluent";
    }
}
