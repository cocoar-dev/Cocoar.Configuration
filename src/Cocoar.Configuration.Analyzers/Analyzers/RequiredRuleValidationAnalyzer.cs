using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cocoar.Configuration.Analyzers.Analyzers;

/// <summary>
/// Analyzer that validates required configuration rules.
/// Warns when required rules reference files or resources that may not exist.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RequiredRuleValidationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RequiredRuleValidation);

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
            AnalyzeRequiredRules(lambda, context);
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

    private static void AnalyzeRequiredRules(SimpleLambdaExpressionSyntax lambda, SyntaxNodeAnalysisContext context)
    {
        // Look for rule chains that include .Required()
        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        
        foreach (var inv in invocations)
        {
            var ruleInfo = ExtractRequiredRuleInfo(inv, context.SemanticModel);
            if (ruleInfo == null)
            {
                continue;
            }

            // Check if the file/resource exists
            if (ruleInfo.FilePath != null && !string.IsNullOrEmpty(ruleInfo.FilePath))
            {
                // Note: In a real implementation, we'd check the project files
                // For now, we'll emit a warning for any required file rule
                // to remind developers to ensure the file exists
                
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.RequiredRuleValidation,
                    ruleInfo.Location,
                    ruleInfo.ConfigurationType?.Name ?? "configuration",
                    ruleInfo.FilePath);

                // Only report if the file path looks absolute or relative (basic heuristic)
                if (ruleInfo.FilePath.Contains(".json") || 
                    ruleInfo.FilePath.Contains(".xml") || 
                    ruleInfo.FilePath.Contains(".yaml"))
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static RequiredRuleInfo? ExtractRequiredRuleInfo(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Look for .Required() calls
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess?.Name.Identifier.Text != "Required")
        {
            return null;
        }

        // Traverse back through the chain to find .For<T>() and .FromFile()
        ITypeSymbol? configurationType = null;
        string? filePath = null;
        Location? location = null;

        var current = invocation.Parent;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax parentInv)
            {
                var parentMember = parentInv.Expression as MemberAccessExpressionSyntax;
                
                // Extract For<T> type
                if (parentMember?.Name is GenericNameSyntax { Identifier.Text: "For" } genericName)
                {
                    var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                    if (typeArg != null)
                    {
                        var typeInfo = semanticModel.GetTypeInfo(typeArg);
                        configurationType = typeInfo.Type;
                        location = parentInv.GetLocation();
                    }
                }

                // Extract FromFile path
                if (parentMember?.Name.Identifier.Text == "FromFile")
                {
                    var arg = parentInv.ArgumentList.Arguments.FirstOrDefault();
                    if (arg?.Expression is LiteralExpressionSyntax literal)
                    {
                        filePath = literal.Token.ValueText;
                    }
                }
            }
            current = current.Parent;
        }

        if (configurationType == null || location == null)
        {
            return null;
        }

        return new RequiredRuleInfo
        {
            ConfigurationType = configurationType,
            FilePath = filePath,
            Location = location
        };
    }

    private class RequiredRuleInfo
    {
        public ITypeSymbol? ConfigurationType { get; init; }
        public string? FilePath { get; init; }
        public required Location Location { get; init; }
    }
}
