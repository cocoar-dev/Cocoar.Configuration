using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cocoar.Configuration.Analyzers.Analyzers;

/// <summary>
/// Validates that configuration rules are ordered correctly.
/// When a rule uses <c>GetConfig&lt;T&gt;()</c> in its provider factory or <c>.When()</c> predicate,
/// the type <c>T</c> must have been registered by an earlier rule.
///
/// Algorithm:
/// 1. Find the rules collection expression: <c>rule => [...]</c>
/// 2. Walk each element in order — each element is one rule
/// 3. For each rule, extract <c>T</c> from <c>rule.For&lt;T&gt;()</c>
/// 4. Scan all lambda arguments in the rule's method chain for <c>GetConfig&lt;X&gt;()</c> calls
/// 5. Check that <c>X</c> was registered by an earlier rule
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RuleOrderingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RuleOrderingViolation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return;

        // Only trigger on UseConfiguration (not AddCocoarConfiguration to avoid double-firing)
        if (ma.Name.Identifier.Text is not "UseConfiguration")
            return;

        // Find collection expressions: rule => [...]
        foreach (var lambda in invocation.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>())
        {
            if (lambda.ExpressionBody is CollectionExpressionSyntax collection)
            {
                AnalyzeRuleCollection(collection, context);
            }
        }
    }

    private static void AnalyzeRuleCollection(CollectionExpressionSyntax collection, SyntaxNodeAnalysisContext context)
    {
        var registeredTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in collection.Elements)
        {
            if (element is not ExpressionElementSyntax exprElement)
                continue;

            // Extract For<T> type from this rule expression
            var (typeName, location) = ExtractForType(exprElement.Expression, context.SemanticModel);
            if (typeName == null || location == null)
                continue;

            // Find all GetConfig<X>() dependencies in this rule's lambdas
            var dependencies = ExtractDependencies(exprElement.Expression, context.SemanticModel);

            foreach (var dep in dependencies)
            {
                if (!registeredTypes.Contains(dep))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.RuleOrderingViolation,
                        location,
                        typeName,
                        dep));
                }
            }

            registeredTypes.Add(typeName);
        }
    }

    /// <summary>
    /// Extracts the type name from <c>rule.For&lt;T&gt;()</c> in the expression.
    /// </summary>
    private static (string? TypeName, Location? Location) ExtractForType(
        ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Walk all invocations in this expression to find For<T>()
        foreach (var inv in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name is GenericNameSyntax { Identifier.Text: "For" } genericName)
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg == null) continue;

                // Semantic resolution
                var typeInfo = semanticModel.GetTypeInfo(typeArg);
                if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
                    return (typeInfo.Type.ToDisplayString(), inv.GetLocation());

                // Syntax fallback (for test fixtures with incomplete references)
                return (typeArg.ToString(), inv.GetLocation());
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Finds all <c>GetConfig&lt;X&gt;()</c> calls in lambda arguments of this rule's method chain.
    /// Scans the first lambda argument of every method in the chain — this naturally covers
    /// provider factories (<c>From*(accessor => ...)</c>) and <c>.When(accessor => ...)</c>
    /// without needing to know method names.
    /// </summary>
    private static List<string> ExtractDependencies(ExpressionSyntax ruleExpression, SemanticModel semanticModel)
    {
        var dependencies = new List<string>();

        foreach (var inv in ruleExpression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            // Skip For<T>() itself
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text == "For")
                continue;

            // Check each argument for lambdas containing GetConfig<X>()
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                SyntaxNode? lambdaBody = arg.Expression switch
                {
                    SimpleLambdaExpressionSyntax simple => (SyntaxNode?)simple.Body ?? simple.ExpressionBody,
                    ParenthesizedLambdaExpressionSyntax paren => (SyntaxNode?)paren.Body ?? paren.ExpressionBody,
                    _ => null
                };

                if (lambdaBody == null) continue;

                foreach (var getConfigCall in lambdaBody.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                {
                    if (getConfigCall.Expression is MemberAccessExpressionSyntax getConfigMa &&
                        getConfigMa.Name is GenericNameSyntax { Identifier.Text: "GetConfig" } getConfigGeneric)
                    {
                        var typeArg = getConfigGeneric.TypeArgumentList.Arguments.FirstOrDefault();
                        if (typeArg == null) continue;

                        var typeInfo = semanticModel.GetTypeInfo(typeArg);
                        if (typeInfo.Type != null && typeInfo.Type.Kind != SymbolKind.ErrorType)
                        {
                            dependencies.Add(typeInfo.Type.ToDisplayString());
                        }
                        else
                        {
                            // Syntax fallback
                            dependencies.Add(typeArg.ToString());
                        }
                    }
                }
            }
        }

        return dependencies;
    }
}
