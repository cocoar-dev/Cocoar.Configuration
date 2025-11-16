using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cocoar.Configuration.Analyzers.Analyzers;

/// <summary>
/// Analyzer that detects multiple unconditional rules for the same configuration type.
/// Warns that only the last rule will be effective (last write wins).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DuplicateUnconditionalRulesAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DuplicateUnconditionalRules);

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
            AnalyzeDuplicates(lambda, context);
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

    private void AnalyzeDuplicates(SimpleLambdaExpressionSyntax lambda, SyntaxNodeAnalysisContext context)
    {
        // Group rules by configuration type
        var rulesByType = new Dictionary<string, List<RuleInfo>>();
        
        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>();
        
        foreach (var inv in invocations)
        {
            var ruleInfo = ExtractRuleInfo(inv, context.SemanticModel);
            if (ruleInfo == null)
            {
                continue;
            }

            var typeName = ruleInfo.ConfigurationType.ToDisplayString();
            if (!rulesByType.ContainsKey(typeName))
            {
                rulesByType[typeName] = new List<RuleInfo>();
            }

            rulesByType[typeName].Add(ruleInfo);
        }

        // Check each type for duplicate unconditional rules
        foreach (var (typeName, rules) in rulesByType)
        {
            if (rules.Count <= 1)
            {
                continue; // No duplicates
            }

            // Count unconditional rules (no .When() clause)
            var unconditionalRules = rules.Where(r => !r.HasCondition).ToList();
            
            if (unconditionalRules.Count > 1)
            {
                // Report diagnostic on all but the last (which will win)
                for (int i = 0; i < unconditionalRules.Count - 1; i++)
                {
                    var rule = unconditionalRules[i];
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateUnconditionalRules,
                        rule.Location,
                        rule.ConfigurationType.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private RuleInfo? ExtractRuleInfo(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Look for .For<T>() pattern
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

        // Check if rule has .When() condition
        bool hasCondition = HasWhenCondition(invocation);

        return new RuleInfo
        {
            ConfigurationType = typeInfo.Type,
            HasCondition = hasCondition,
            Location = invocation.GetLocation()
        };
    }

    private bool HasWhenCondition(InvocationExpressionSyntax invocation)
    {
        // Traverse up the chain looking for .When()
        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInv)
            {
                var memberAccess = parentInv.Expression as MemberAccessExpressionSyntax;
                if (memberAccess?.Name.Identifier.Text == "When")
                {
                    return true;
                }
            }
            parent = parent.Parent;
        }
        return false;
    }

    private class RuleInfo
    {
        public required ITypeSymbol ConfigurationType { get; init; }
        public required bool HasCondition { get; init; }
        public required Location Location { get; init; }
    }
}
