using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cocoar.Configuration.Analyzers.Analyzers;

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
        // Only analyze invocations that look like AddCocoarConfiguration to avoid scanning unrelated calls.
        // Fallback heuristic: if semantic model cannot resolve symbol, fall back to simple name text match.
        if (!IsAddCocoarConfigurationCall(invocation, context.SemanticModel) && !LooksLikeAddCocoarConfiguration(invocation))
        {
            return;
        }

        var lambdas = invocation.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>();
        foreach (var lambda in lambdas)
        {
            AnalyzeRuleOrdering(lambda, context);
        }
    }

    private bool IsAddCocoarConfigurationCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null)
        {
            return false;
        }

        var methodSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
        return methodSymbol?.Name == "AddCocoarConfiguration";
    }

    private static bool LooksLikeAddCocoarConfiguration(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax ma)
        {
            return ma.Name.Identifier.Text == "AddCocoarConfiguration";
        }
        return false;
    }

    private void AnalyzeRuleOrdering(SimpleLambdaExpressionSyntax lambda, SyntaxNodeAnalysisContext context)
    {
        var configuredTypes = new HashSet<string>();
        
        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        
        foreach (var inv in invocations)
        {
            var ruleInfo = ExtractRuleInfo(inv, context.SemanticModel);
            if (ruleInfo == null)
            {
                continue;
            }

            var dependencies = ExtractDependencies(inv, context.SemanticModel);
            
            foreach (var dependency in dependencies)
            {
                if (!configuredTypes.Contains(dependency))
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.RuleOrderingViolation,
                        ruleInfo.Location,
                        ruleInfo.ConfigurationType.Name,
                        dependency);

                    context.ReportDiagnostic(diagnostic);
                }
            }

            configuredTypes.Add(ruleInfo.ConfigurationType.ToDisplayString());
        }
    }

    private RuleInfo? ExtractRuleInfo(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess?.Name.Identifier.Text != "For")
        {
            return null;
        }

        if (memberAccess.Name is not GenericNameSyntax genericName)
        {
            return null;
        }

        var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
        if (typeArg == null)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeArg);
        if (typeInfo.Type == null)
        {
            return null;
        }

        return new RuleInfo
        {
            ConfigurationType = typeInfo.Type,
            Location = invocation.GetLocation()
        };
    }

    private List<string> ExtractDependencies(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var dependencies = new List<string>();

        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInv)
            {
                var memberAccess = parentInv.Expression as MemberAccessExpressionSyntax;
                
                if (memberAccess?.Name.Identifier.Text == "When")
                {
                    var lambdaArg = parentInv.ArgumentList.Arguments.FirstOrDefault()?.Expression as SimpleLambdaExpressionSyntax;
                    if (lambdaArg != null)
                    {
                        dependencies.AddRange(ExtractGetRequiredConfigTypes(lambdaArg, semanticModel));
                    }
                }

                if (memberAccess?.Name.Identifier.Text is "FromHttpPolling" or "FromStatic")
                {
                    var lambdaArg = parentInv.ArgumentList.Arguments.FirstOrDefault()?.Expression as SimpleLambdaExpressionSyntax;
                    if (lambdaArg != null)
                    {
                        dependencies.AddRange(ExtractGetRequiredConfigTypes(lambdaArg, semanticModel));
                    }
                }
            }
            parent = parent.Parent;
        }

        return dependencies;
    }

    private List<string> ExtractGetRequiredConfigTypes(SimpleLambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        var types = new List<string>();

        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>();
        
        foreach (var inv in invocations)
        {
            var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
            if (memberAccess?.Name is GenericNameSyntax { Identifier.Text: "GetRequiredConfig" } genericName)
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                {
                    var typeInfo = semanticModel.GetTypeInfo(typeArg);
                    if (typeInfo.Type != null)
                    {
                        types.Add(typeInfo.Type.ToDisplayString());
                    }
                }
            }
        }

        return types;
    }

    private class RuleInfo
    {
        public required ITypeSymbol ConfigurationType { get; init; }
        public required Location Location { get; init; }
    }
}
