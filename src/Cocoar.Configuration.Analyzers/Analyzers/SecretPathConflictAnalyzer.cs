using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cocoar.Configuration.Analyzers.Analyzers;

/// <summary>
/// Analyzer that detects secret path conflicts in configuration rules.
/// Warns when a non-secret property might conflict with a Secret&lt;T&gt; property.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SecretPathConflictAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.SecretPathConflict);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for invocation expressions (method calls like rule.For<T>())
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Look for AddCocoarConfiguration calls
        if (!IsAddCocoarConfigurationCall(invocation, context.SemanticModel))
        {
            return;
        }

        // Extract lambda parameter (the rules builder)
        var lambdas = invocation.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>();
        foreach (var lambda in lambdas)
        {
            AnalyzeRules(lambda, context);
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

    private void AnalyzeRules(SimpleLambdaExpressionSyntax lambda, SyntaxNodeAnalysisContext context)
    {
        // Look for rule.For<T>() calls
        var invocations = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>();
        
        var rules = new List<RuleInfo>();
        
        foreach (var inv in invocations)
        {
            var ruleInfo = ExtractRuleInfo(inv, context.SemanticModel);
            if (ruleInfo != null)
            {
                rules.Add(ruleInfo);
            }
        }

        // Check for path conflicts between secrets and non-secrets
        DetectPathConflicts(rules, context);
    }

    private RuleInfo? ExtractRuleInfo(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Look for .For<T>() pattern
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess?.Name.Identifier.Text != "For")
        {
            return null;
        }

        // Extract type argument from For<T>
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

        // Extract select path if present
        string? selectPath = ExtractSelectPath(invocation);

        return new RuleInfo
        {
            ConfigurationType = typeInfo.Type,
            SelectPath = selectPath,
            Location = invocation.GetLocation()
        };
    }

    private string? ExtractSelectPath(InvocationExpressionSyntax invocation)
    {
        // Look for .Select("path") in the chain
        var parent = invocation.Parent;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax parentInv)
            {
                var memberAccess = parentInv.Expression as MemberAccessExpressionSyntax;
                if (memberAccess?.Name.Identifier.Text == "Select")
                {
                    // Extract string argument
                    var arg = parentInv.ArgumentList.Arguments.FirstOrDefault();
                    if (arg?.Expression is LiteralExpressionSyntax literal)
                    {
                        return literal.Token.ValueText;
                    }
                }
            }
            parent = parent.Parent;
        }
        return null;
    }

    private void DetectPathConflicts(List<RuleInfo> rules, SyntaxNodeAnalysisContext context)
    {
        // Group rules by configuration type
        var rulesByType = rules.GroupBy(r => r.ConfigurationType.ToDisplayString());

        foreach (var group in rulesByType)
        {
            var typeSymbol = group.First().ConfigurationType;
            var hasSecretProperties = HasSecretProperties(typeSymbol);

            if (!hasSecretProperties)
            {
                continue; // No secrets, no conflict possible
            }

            // Check each rule's select path against secret property paths
            foreach (var rule in group)
            {
                if (rule.SelectPath == null)
                {
                    continue;
                }

                // Check if select path targets a property that should be Secret<T>
                var secretPropertyPath = FindConflictingSecretProperty(typeSymbol, rule.SelectPath);
                if (secretPropertyPath != null)
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.SecretPathConflict,
                        rule.Location,
                        rule.SelectPath,
                        secretPropertyPath);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private bool HasSecretProperties(ITypeSymbol typeSymbol)
    {
        // Check if type has any properties of type Secret<T>
        var members = typeSymbol.GetMembers().OfType<IPropertySymbol>();
        return members.Any(p => IsSecretType(p.Type));
    }

    private bool IsSecretType(ITypeSymbol typeSymbol)
    {
        // Check if type is Secret<T> from Cocoar.Configuration.Secrets
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return namedType.Name == "Secret" && 
               namedType.ContainingNamespace?.ToDisplayString().StartsWith("Cocoar.Configuration") == true;
    }

    private string? FindConflictingSecretProperty(ITypeSymbol typeSymbol, string selectPath)
    {
        // Simple path matching - look for properties with Secret<T> type
        var properties = typeSymbol.GetMembers().OfType<IPropertySymbol>();
        
        foreach (var prop in properties)
        {
            if (IsSecretType(prop.Type))
            {
                // Check if selectPath could conflict with this secret property
                if (selectPath.IndexOf(prop.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return $"{typeSymbol.Name}.{prop.Name}";
                }
            }
        }

        return null;
    }

    private class RuleInfo
    {
        public required ITypeSymbol ConfigurationType { get; init; }
        public string? SelectPath { get; init; }
        public required Location Location { get; init; }
    }
}
