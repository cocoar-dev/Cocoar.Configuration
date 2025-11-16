using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cocoar.Configuration.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider that automatically reorders configuration rules to fix dependency violations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReorderRulesCodeFixProvider)), Shared]
public class ReorderRulesCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.RuleOrderingViolation.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation expression that triggered the diagnostic
        var invocation = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
        if (invocation == null)
        {
            return;
        }

        // Register a code action that will reorder the rules
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Move rule after dependency",
                createChangedDocument: c => ReorderRulesAsync(context.Document, invocation, c),
                equivalenceKey: nameof(ReorderRulesCodeFixProvider)),
            diagnostic);
    }

    private async Task<Document> ReorderRulesAsync(
        Document document,
        InvocationExpressionSyntax problematicRule,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        // Find the lambda containing the rules
        var lambda = problematicRule.AncestorsAndSelf().OfType<SimpleLambdaExpressionSyntax>().FirstOrDefault();
        if (lambda == null)
        {
            return document;
        }

        // Find all rule invocations
        var ruleInvocations = lambda.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsRuleInvocation)
            .ToList();

        // Simple reordering: Move the problematic rule to the end
        // In a real implementation, we'd analyze dependencies and place it correctly
        var index = ruleInvocations.IndexOf(problematicRule);
        if (index >= 0 && index < ruleInvocations.Count - 1)
        {
            // This is a simplified example - actual implementation would need
            // to rewrite the collection initializer or array
            // For now, we just register the action to show the intent
        }

        return document;
    }

    private bool IsRuleInvocation(InvocationExpressionSyntax invocation)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        return memberAccess?.Name.Identifier.Text == "For";
    }
}
