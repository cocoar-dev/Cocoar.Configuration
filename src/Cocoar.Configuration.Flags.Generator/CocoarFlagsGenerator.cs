using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cocoar.Configuration.Flags.Generator;

[Generator]
public sealed class CocoarFlagsGenerator : IIncrementalGenerator
{
    private const string FeatureFlagsBaseTypeFqn = "Cocoar.Configuration.Flags.FeatureFlags";
    private const string EntitlementsBaseTypeFqn = "Cocoar.Configuration.Flags.Entitlements";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classInfos = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is InvocationExpressionSyntax inv
                    && inv.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax g }
                    && g.Identifier.Text == "Register"
                    && g.TypeArgumentList.Arguments.Count == 1,
                transform: static (ctx, ct) => ExtractClassInfoFromInvocation(ctx, ct))
            .Where(static x => x is not null);

        var collected = classInfos.Collect();

        context.RegisterSourceOutput(collected, static (spc, items) =>
        {
            var validItems = items.Where(x => x is not null).ToList();
            if (validItems.Count == 0) return;

            // Emit diagnostics
            foreach (var item in validItems)
            {
                foreach (var diag in item!.Diagnostics)
                    spc.ReportDiagnostic(diag);
            }

            // Deduplicate by FullTypeName (same type may be registered multiple times)
            var flagItems = validItems
                .Where(x => x!.IsFlags)
                .GroupBy(x => x!.FullTypeName)
                .Select(g => g.First())
                .ToList();

            var entitlementItems = validItems
                .Where(x => !x!.IsFlags)
                .GroupBy(x => x!.FullTypeName)
                .Select(g => g.First())
                .ToList();

            if (flagItems.Count == 0 && entitlementItems.Count == 0) return;

            var source = GenerateSource(flagItems!, entitlementItems!);
            spc.AddSource("CocoarFlagsGenerated.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static ClassInfo? ExtractClassInfoFromInvocation(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var genericName = (GenericNameSyntax)memberAccess.Name;
        var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];

        var typeSymbol = ctx.SemanticModel.GetTypeInfo(typeArgSyntax, ct).Type as INamedTypeSymbol;
        if (typeSymbol == null || typeSymbol.IsAbstract)
            return null;

        if (InheritsFrom(typeSymbol, FeatureFlagsBaseTypeFqn))
        {
            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (syntaxRef.GetSyntax(ct) is not ClassDeclarationSyntax classDecl) continue;
                var sm = ctx.SemanticModel.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                return ExtractFlagsClassInfo(classDecl, typeSymbol, sm);
            }
            // Type from a referenced assembly — no source available
            return new ClassInfo(true, typeSymbol.ToDisplayString(), DateTimeOffset.MinValue,
                new List<MemberInfo>(), new List<Diagnostic>());
        }

        if (InheritsFrom(typeSymbol, EntitlementsBaseTypeFqn))
        {
            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (syntaxRef.GetSyntax(ct) is not ClassDeclarationSyntax classDecl) continue;
                var sm = ctx.SemanticModel.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                return ExtractEntitlementsClassInfo(classDecl, typeSymbol, sm);
            }
            return new ClassInfo(false, typeSymbol.ToDisplayString(), DateTimeOffset.MinValue,
                new List<MemberInfo>(), new List<Diagnostic>());
        }

        return null;
    }

    private static bool InheritsFrom(INamedTypeSymbol symbol, string baseTypeFqn)
    {
        var current = symbol.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == baseTypeFqn)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    // ─── FeatureFlags extraction ──────────────────────────────────────────────

    private static ClassInfo ExtractFlagsClassInfo(
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol symbol,
        SemanticModel semanticModel)
    {
        var diagnostics = new List<Diagnostic>();
        var fullTypeName = symbol.ToDisplayString();
        var classExpiresAt = ExtractClassExpiresAt(classDecl, semanticModel, fullTypeName, diagnostics);

        var flags = ExtractFlagDefinitions(classDecl, semanticModel, classExpiresAt, fullTypeName, diagnostics);

        return new ClassInfo(
            isFlags: true,
            fullTypeName: fullTypeName,
            expiresAt: classExpiresAt,
            members: flags,
            diagnostics: diagnostics);
    }

    private static DateTimeOffset ExtractClassExpiresAt(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        string typeName,
        List<Diagnostic> diagnostics)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop) continue;
            if (prop.Identifier.Text != "ExpiresAt") continue;

            ExpressionSyntax? expr = null;
            if (prop.ExpressionBody?.Expression is not null)
                expr = prop.ExpressionBody.Expression;
            else if (prop.Initializer?.Value is not null)
                expr = prop.Initializer.Value;

            if (expr != null)
            {
                var result = TryParseDateTimeOffsetLiteral(expr);
                if (result.HasValue) return result.Value;
            }

            // Could not statically determine
            diagnostics.Add(Diagnostic.Create(
                FlagsGeneratorDiagnostics.NonStaticExpiresAt,
                prop.GetLocation(),
                typeName));
            return DateTimeOffset.MinValue;
        }

        // ExpiresAt not found in this partial — MinValue fallback
        diagnostics.Add(Diagnostic.Create(
            FlagsGeneratorDiagnostics.NonStaticExpiresAt,
            classDecl.Identifier.GetLocation(),
            typeName));
        return DateTimeOffset.MinValue;
    }

    private static List<MemberInfo> ExtractFlagDefinitions(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        DateTimeOffset classExpiresAt,
        string typeName,
        List<Diagnostic> diagnostics)
    {
        var flags = new List<MemberInfo>();

        foreach (var invocation in classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsCallTo(invocation, "DefineFlag")) continue;
            if (invocation.ArgumentList.Arguments.Count < 2) continue;

            var nameArg = invocation.ArgumentList.Arguments[0].Expression;
            var name = ExtractNameofArgument(nameArg);
            if (name == null) continue;

            var expiresAt = classExpiresAt;
            var description = (string?)null;

            foreach (var arg in invocation.ArgumentList.Arguments.Skip(1))
            {
                var argName = arg.NameColon?.Name.Identifier.Text;

                if (argName == "expiresAt")
                {
                    var parsed = TryParseDateTimeOffsetLiteral(arg.Expression);
                    if (parsed.HasValue)
                    {
                        expiresAt = parsed.Value;
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic.Create(
                            FlagsGeneratorDiagnostics.NonStaticFlagExpiresAt,
                            arg.GetLocation(),
                            typeName + ".DefineFlag"));
                    }
                }
                else if (argName == "description")
                {
                    description = TryExtractStringLiteral(arg.Expression);
                    if (description == null && !IsNullLiteral(arg.Expression))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            FlagsGeneratorDiagnostics.NonLiteralDescription,
                            arg.GetLocation(),
                            typeName + ".DefineFlag"));
                    }
                }
            }

            flags.Add(new MemberInfo(name, expiresAt, description));
        }

        return flags;
    }

    // ─── Entitlements extraction ──────────────────────────────────────────────

    private static ClassInfo ExtractEntitlementsClassInfo(
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol symbol,
        SemanticModel semanticModel)
    {
        var diagnostics = new List<Diagnostic>();
        var fullTypeName = symbol.ToDisplayString();
        var entitlements = ExtractEntitlementDefinitions(classDecl, semanticModel, fullTypeName, diagnostics);

        return new ClassInfo(
            isFlags: false,
            fullTypeName: fullTypeName,
            expiresAt: DateTimeOffset.MinValue,
            members: entitlements,
            diagnostics: diagnostics);
    }

    private static List<MemberInfo> ExtractEntitlementDefinitions(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        string typeName,
        List<Diagnostic> diagnostics)
    {
        var entitlements = new List<MemberInfo>();

        foreach (var invocation in classDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsCallTo(invocation, "DefineEntitlement")) continue;
            if (invocation.ArgumentList.Arguments.Count < 2) continue;

            var nameArg = invocation.ArgumentList.Arguments[0].Expression;
            var name = ExtractNameofArgument(nameArg);
            if (name == null) continue;

            var description = (string?)null;

            foreach (var arg in invocation.ArgumentList.Arguments.Skip(1))
            {
                var argName = arg.NameColon?.Name.Identifier.Text;
                if (argName == "description")
                {
                    description = TryExtractStringLiteral(arg.Expression);
                    if (description == null && !IsNullLiteral(arg.Expression))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            FlagsGeneratorDiagnostics.NonLiteralDescription,
                            arg.GetLocation(),
                            typeName + ".DefineEntitlement"));
                    }
                }
            }

            entitlements.Add(new MemberInfo(name, DateTimeOffset.MinValue, description));
        }

        return entitlements;
    }

    // ─── Syntax helpers ───────────────────────────────────────────────────────

    private static bool IsCallTo(InvocationExpressionSyntax invocation, string methodName)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text == methodName,
            GenericNameSyntax generic => generic.Identifier.Text == methodName,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text == methodName
                || (memberAccess.Name is GenericNameSyntax gn && gn.Identifier.Text == methodName),
            _ => false
        };
    }

    private static string? ExtractNameofArgument(ExpressionSyntax expr)
    {
        if (expr is InvocationExpressionSyntax inv
            && inv.Expression is IdentifierNameSyntax idName
            && idName.Identifier.Text == "nameof"
            && inv.ArgumentList.Arguments.Count == 1)
        {
            var arg = inv.ArgumentList.Arguments[0].Expression;
            return arg switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => null
            };
        }
        return null;
    }

    private static DateTimeOffset? TryParseDateTimeOffsetLiteral(ExpressionSyntax expr)
    {
        ArgumentListSyntax? args = expr switch
        {
            ObjectCreationExpressionSyntax oce => oce.ArgumentList,
            ImplicitObjectCreationExpressionSyntax ioce => ioce.ArgumentList,
            _ => null
        };

        if (args == null || args.Arguments.Count < 3) return null;

        // Extract integer arguments (at least year, month, day)
        var ints = new List<int>();
        foreach (var arg in args.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit
                && lit.Token.Value is int v)
            {
                ints.Add(v);
            }
            else
            {
                // Non-integer argument — could be TimeSpan.Zero at position 6 or 3
                // We stop collecting ints but still try to parse if we have 3+
                break;
            }
        }

        if (ints.Count < 3) return null;

        try
        {
            return ints.Count >= 7
                ? new DateTimeOffset(ints[0], ints[1], ints[2], ints[3], ints[4], ints[5], TimeSpan.Zero)
                : ints.Count >= 6
                    ? new DateTimeOffset(ints[0], ints[1], ints[2], ints[3], ints[4], ints[5], TimeSpan.Zero)
                    : ints.Count == 3
                        ? new DateTimeOffset(ints[0], ints[1], ints[2], 0, 0, 0, TimeSpan.Zero)
                        : (DateTimeOffset?)null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractStringLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax lit
               && lit.IsKind(SyntaxKind.StringLiteralExpression)
            ? lit.Token.ValueText
            : null;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression)
               || expr is IdentifierNameSyntax id && id.Identifier.Text == "null";
    }

    // ─── Code generation ──────────────────────────────────────────────────────

    private static string GenerateSource(List<ClassInfo> flagClasses, List<ClassInfo> entitlementClasses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Cocoar.Configuration.Flags.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class CocoarFlagsDescriptors");
        sb.AppendLine("    {");

        // Flags dictionary
        sb.AppendLine("        internal static readonly global::System.Collections.Generic.IReadOnlyDictionary<");
        sb.AppendLine("            global::System.Type,");
        sb.AppendLine("            global::Cocoar.Configuration.Flags.FeatureFlagClassDescriptor> Flags =");
        sb.AppendLine("            new global::System.Collections.Generic.Dictionary<");
        sb.AppendLine("                global::System.Type,");
        sb.AppendLine("                global::Cocoar.Configuration.Flags.FeatureFlagClassDescriptor>");
        sb.AppendLine("            {");
        foreach (var cls in flagClasses)
        {
            sb.AppendLine("                {");
            sb.AppendLine($"                    typeof(global::{cls.FullTypeName}),");
            sb.AppendLine($"                    new global::Cocoar.Configuration.Flags.FeatureFlagClassDescriptor(");
            sb.AppendLine($"                        Type: typeof(global::{cls.FullTypeName}),");
            sb.AppendLine($"                        ExpiresAt: {RenderDateTimeOffset(cls.ExpiresAt)},");
            sb.AppendLine("                        Flags: new global::Cocoar.Configuration.Flags.FlagDefinitionDescriptor[]");
            sb.AppendLine("                        {");
            foreach (var flag in cls.Members)
            {
                var descArg = flag.Description != null
                    ? $"\"{EscapeString(flag.Description)}\""
                    : "null";
                sb.AppendLine($"                            new(\"{flag.Name}\", {RenderDateTimeOffset(flag.ExpiresAt)}, {descArg}),");
            }
            sb.AppendLine("                        })");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            };");
        sb.AppendLine();

        // Entitlements dictionary
        sb.AppendLine("        internal static readonly global::System.Collections.Generic.IReadOnlyDictionary<");
        sb.AppendLine("            global::System.Type,");
        sb.AppendLine("            global::Cocoar.Configuration.Flags.EntitlementClassDescriptor> Entitlements =");
        sb.AppendLine("            new global::System.Collections.Generic.Dictionary<");
        sb.AppendLine("                global::System.Type,");
        sb.AppendLine("                global::Cocoar.Configuration.Flags.EntitlementClassDescriptor>");
        sb.AppendLine("            {");
        foreach (var cls in entitlementClasses)
        {
            sb.AppendLine("                {");
            sb.AppendLine($"                    typeof(global::{cls.FullTypeName}),");
            sb.AppendLine($"                    new global::Cocoar.Configuration.Flags.EntitlementClassDescriptor(");
            sb.AppendLine($"                        Type: typeof(global::{cls.FullTypeName}),");
            sb.AppendLine("                        Entitlements: new global::Cocoar.Configuration.Flags.EntitlementDefinitionDescriptor[]");
            sb.AppendLine("                        {");
            foreach (var ent in cls.Members)
            {
                var descArg = ent.Description != null
                    ? $"\"{EscapeString(ent.Description)}\""
                    : "null";
                sb.AppendLine($"                            new(\"{ent.Name}\", {descArg}),");
            }
            sb.AppendLine("                        })");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderDateTimeOffset(DateTimeOffset dto)
    {
        if (dto == DateTimeOffset.MinValue)
            return "global::System.DateTimeOffset.MinValue";

        return $"new global::System.DateTimeOffset({dto.Year}, {dto.Month}, {dto.Day}, {dto.Hour}, {dto.Minute}, {dto.Second}, global::System.TimeSpan.Zero)";
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ─── Data models ──────────────────────────────────────────────────────────

    private sealed class ClassInfo
    {
        public bool IsFlags { get; }
        public string FullTypeName { get; }
        public DateTimeOffset ExpiresAt { get; }
        public List<MemberInfo> Members { get; }
        public List<Diagnostic> Diagnostics { get; }

        public ClassInfo(bool isFlags, string fullTypeName, DateTimeOffset expiresAt, List<MemberInfo> members, List<Diagnostic> diagnostics)
        {
            IsFlags = isFlags;
            FullTypeName = fullTypeName;
            ExpiresAt = expiresAt;
            Members = members;
            Diagnostics = diagnostics;
        }
    }

    private sealed class MemberInfo
    {
        public string Name { get; }
        public DateTimeOffset ExpiresAt { get; }
        public string? Description { get; }

        public MemberInfo(string name, DateTimeOffset expiresAt, string? description)
        {
            Name = name;
            ExpiresAt = expiresAt;
            Description = description;
        }
    }
}
