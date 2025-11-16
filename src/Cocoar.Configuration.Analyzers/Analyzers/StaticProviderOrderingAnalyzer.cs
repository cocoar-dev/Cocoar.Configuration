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
            var providerInfo = ExtractProviderInfo(inv);
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

    private static (bool isStatic, Location location)? ExtractProviderInfo(InvocationExpressionSyntax invocation)
    {
        // Look for provider methods in the chain
        var parent = invocation;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInv)
            {
                var memberAccess = parentInv.Expression as MemberAccessExpressionSyntax;
                if (memberAccess != null)
                {
                    var methodName = memberAccess.Name.Identifier.Text;
                    
                    // Check if this is a provider method
                    if (IsProviderMethod(methodName))
                    {
                        bool isStatic = methodName.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0;
                        return (isStatic, parentInv.GetLocation());
                    }
                }
            }
            parent = parent.Parent as InvocationExpressionSyntax;
        }

        return null;
    }

    private static bool IsProviderMethod(string methodName)
    {
        return methodName is "FromFile" 
            or "FromEnvironment" 
            or "FromCommandLine"
            or "FromStatic" 
            or "FromObservable"
            or "FromHttpPolling"
            or "FromMicrosoftConfiguration";
    }
}
