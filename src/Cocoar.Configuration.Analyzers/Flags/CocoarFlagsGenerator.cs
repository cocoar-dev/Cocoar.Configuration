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
    private const string IFeatureFlagsFqn = "Cocoar.Configuration.Flags.IFeatureFlags";
    private const string IEntitlementsFqn = "Cocoar.Configuration.Flags.IEntitlements";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Pipeline 1: Descriptor generation (existing) ────────────────────
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
                .OrderBy(x => x!.FullTypeName, StringComparer.Ordinal)
                .ToList();

            var entitlementItems = validItems
                .Where(x => !x!.IsFlags)
                .GroupBy(x => x!.FullTypeName)
                .Select(g => g.First())
                .OrderBy(x => x!.FullTypeName, StringComparer.Ordinal)
                .ToList();

            if (flagItems.Count == 0 && entitlementItems.Count == 0) return;

            var source = GenerateSource(flagItems!, entitlementItems!);
            spc.AddSource("CocoarFlagsGenerated.g.cs", SourceText.From(source, Encoding.UTF8));
        });

        // ── Pipeline 2: Partial class generation for IFeatureFlags<T> / IEntitlements<T> ──
        var partialClassInfos = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax cls
                    && cls.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && cls.BaseList is not null,
                transform: static (ctx, ct) => ExtractPartialClassInfo(ctx, ct))
            .Where(static x => x is not null);

        context.RegisterSourceOutput(partialClassInfos, static (spc, info) =>
        {
            if (info is null) return;
            var source = GeneratePartialClassSource(info);
            spc.AddSource($"{info.ClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static ClassInfo? ExtractClassInfoFromInvocation(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var genericName = (GenericNameSyntax)memberAccess.Name;
        var typeArgSyntax = genericName.TypeArgumentList.Arguments[0];

        var typeSymbol = ctx.SemanticModel.GetTypeInfo(typeArgSyntax, ct).Type as INamedTypeSymbol;
        if (typeSymbol == null)
            return null;

        if (typeSymbol.IsAbstract)
        {
            // Return a ClassInfo with only a diagnostic — no members, no code generation
            return new ClassInfo(
                isFlags: InheritsFrom(typeSymbol, FeatureFlagsBaseTypeFqn),
                fullTypeName: typeSymbol.ToDisplayString(),
                expiresAt: DateTimeOffset.MinValue,
                members: new List<MemberInfo>(),
                diagnostics: new List<Diagnostic>
                {
                    Diagnostic.Create(
                        FlagsGeneratorDiagnostics.AbstractTypeRegistered,
                        typeArgSyntax.GetLocation(),
                        typeSymbol.ToDisplayString())
                });
        }

        if (InheritsFrom(typeSymbol, FeatureFlagsBaseTypeFqn))
        {
            // Iterate ALL partial declarations to collect every FeatureFlag<> property
            var allMembers = new List<MemberInfo>();
            var diagnostics = new List<Diagnostic>();
            var expiresAt = DateTimeOffset.MinValue;
            var fullTypeName = typeSymbol.ToDisplayString();
            var foundSource = false;

            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (syntaxRef.GetSyntax(ct) is not ClassDeclarationSyntax classDecl) continue;
                foundSource = true;
                var sm = ctx.SemanticModel.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);

                // ExpiresAt: take from whichever partial declares it
                if (expiresAt == DateTimeOffset.MinValue)
                    expiresAt = ExtractClassExpiresAt(classDecl, sm, fullTypeName, diagnostics);

                allMembers.AddRange(ExtractFlagProperties(classDecl, fullTypeName, diagnostics));
            }

            if (!foundSource)
            {
                // Type from a referenced assembly — no source available
                return new ClassInfo(true, fullTypeName, DateTimeOffset.MinValue,
                    new List<MemberInfo>(), new List<Diagnostic>());
            }

            // Deduplicate by property name (same property may appear in multiple partials' trivia)
            var dedupedMembers = allMembers
                .GroupBy(m => m.Name)
                .Select(g => g.First())
                .ToList();

            return new ClassInfo(true, fullTypeName, expiresAt, dedupedMembers, diagnostics);
        }

        if (InheritsFrom(typeSymbol, EntitlementsBaseTypeFqn))
        {
            // Iterate ALL partial declarations to collect every Entitlement<> property
            var allMembers = new List<MemberInfo>();
            var diagnostics = new List<Diagnostic>();
            var fullTypeName = typeSymbol.ToDisplayString();
            var foundSource = false;

            foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (syntaxRef.GetSyntax(ct) is not ClassDeclarationSyntax classDecl) continue;
                foundSource = true;
                var sm = ctx.SemanticModel.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                allMembers.AddRange(ExtractEntitlementProperties(classDecl, fullTypeName, diagnostics));
            }

            if (!foundSource)
            {
                return new ClassInfo(false, fullTypeName, DateTimeOffset.MinValue,
                    new List<MemberInfo>(), new List<Diagnostic>());
            }

            var dedupedMembers = allMembers
                .GroupBy(m => m.Name)
                .Select(g => g.First())
                .ToList();

            return new ClassInfo(false, fullTypeName, DateTimeOffset.MinValue, dedupedMembers, diagnostics);
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

    private static List<MemberInfo> ExtractFlagProperties(
        ClassDeclarationSyntax classDecl, string fullTypeName, List<Diagnostic> diagnostics)
    {
        var flags = new List<MemberInfo>();
        foreach (var member in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!IsFlagPropertyType(member.Type)) continue;
            var description = GetXmlSummary(member);
            if (description == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    FlagsGeneratorDiagnostics.MissingPropertyDescription,
                    member.Identifier.GetLocation(),
                    member.Identifier.Text,
                    fullTypeName));
            }
            flags.Add(new MemberInfo(member.Identifier.Text, description));
        }
        return flags;
    }

    // ─── Entitlements extraction ──────────────────────────────────────────────

    private static List<MemberInfo> ExtractEntitlementProperties(
        ClassDeclarationSyntax classDecl, string fullTypeName, List<Diagnostic> diagnostics)
    {
        var entitlements = new List<MemberInfo>();
        foreach (var member in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!IsEntitlementPropertyType(member.Type)) continue;
            var description = GetXmlSummary(member);
            if (description == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    FlagsGeneratorDiagnostics.MissingPropertyDescription,
                    member.Identifier.GetLocation(),
                    member.Identifier.Text,
                    fullTypeName));
            }
            entitlements.Add(new MemberInfo(member.Identifier.Text, description));
        }
        return entitlements;
    }

    // ─── XML doc helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the text content of the &lt;summary&gt; XML doc comment on a property,
    /// or returns null if no summary is present.
    /// </summary>
    private static string? GetXmlSummary(PropertyDeclarationSyntax prop)
    {
        var docTrivia = prop.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (docTrivia is null) return null;

        var summary = docTrivia.ChildNodes()
            .OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "summary");

        if (summary is null) return null;

        var text = string.Concat(summary.Content.OfType<XmlTextSyntax>()
            .SelectMany(t => t.TextTokens)
            .Select(t => t.ValueText));

        // Normalize: trim each line and join with a single space
        var lines = text.Split('\n')
            .Select(l => l.TrimStart().TrimStart('/').Trim())
            .Where(l => l.Length > 0);

        var result = string.Join(" ", lines);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    // ─── Syntax helpers ───────────────────────────────────────────────────────

    private static bool IsFlagPropertyType(TypeSyntax typeSyntax)
        => GetBaseTypeName(typeSyntax) == "FeatureFlag";

    private static bool IsEntitlementPropertyType(TypeSyntax typeSyntax)
        => GetBaseTypeName(typeSyntax) == "Entitlement";

    private static string? GetBaseTypeName(TypeSyntax typeSyntax) => typeSyntax switch
    {
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax { Right: GenericNameSyntax gn } => gn.Identifier.Text,
        _ => null
    };

    private static DateTimeOffset? TryParseDateTimeOffsetLiteral(ExpressionSyntax expr)
    {
        ArgumentListSyntax? args = expr switch
        {
            ObjectCreationExpressionSyntax oce => oce.ArgumentList,
            ImplicitObjectCreationExpressionSyntax ioce => ioce.ArgumentList,
            _ => null
        };

        if (args == null || args.Arguments.Count < 1) return null;

        // Handle: new DateTimeOffset(new DateTime(year, month, day, ...), TimeSpan.Zero)
        var firstArg = args.Arguments[0].Expression;
        if (firstArg is ObjectCreationExpressionSyntax innerOce
            && IsDateTimeTypeName(innerOce.Type))
        {
            return TryParseDateTimeLiteralArgs(innerOce.ArgumentList);
        }
        if (firstArg is ImplicitObjectCreationExpressionSyntax && args.Arguments.Count >= 2)
        {
            // new DateTimeOffset(new(...), TimeSpan.Zero) — can't verify type without semantic model,
            // but if the outer type is DateTimeOffset and first arg is implicit new with 3+ int args, try it
            if (firstArg is ImplicitObjectCreationExpressionSyntax innerIoce)
                return TryParseDateTimeLiteralArgs(innerIoce.ArgumentList);
        }

        if (args.Arguments.Count < 3) return null;

        // Handle: new DateTimeOffset(year, month, day, h, m, s, TimeSpan.Zero)
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
            return ints.Count >= 6
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

    private static DateTimeOffset? TryParseDateTimeLiteralArgs(ArgumentListSyntax? args)
    {
        if (args == null || args.Arguments.Count < 3) return null;

        var ints = new List<int>();
        foreach (var arg in args.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit && lit.Token.Value is int v)
                ints.Add(v);
            else
                break;
        }

        if (ints.Count < 3) return null;

        try
        {
            return ints.Count >= 6
                ? new DateTimeOffset(ints[0], ints[1], ints[2], ints[3], ints[4], ints[5], TimeSpan.Zero)
                : new DateTimeOffset(ints[0], ints[1], ints[2], 0, 0, 0, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDateTimeTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text == "DateTime",
        QualifiedNameSyntax q => q.Right.Identifier.Text == "DateTime",
        _ => false
    };

    private static string EscapeString(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
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
                sb.AppendLine($"                            new(\"{flag.Name}\", {descArg}),");
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

    // ─── Pipeline 2: Partial class extraction ──────────────────────────────

    private static PartialClassInfo? ExtractPartialClassInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (symbol is null) return null;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType || iface.TypeArguments.Length != 1) continue;

            var unboundName = iface.ConstructedFrom.ToDisplayString();
            bool isFlags = unboundName == IFeatureFlagsFqn + "<TConfig>";
            bool isEntitlements = unboundName == IEntitlementsFqn + "<TConfig>";
            if (!isFlags && !isEntitlements) continue;

            var configTypeArg = iface.TypeArguments[0];
            var configTypeFqn = configTypeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var containingNamespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var baseTypeFqn = isFlags ? FeatureFlagsBaseTypeFqn : EntitlementsBaseTypeFqn;
            var alreadyInheritsBase = InheritsFrom(symbol, baseTypeFqn);

            // Collect containing type hierarchy for nested classes
            var containingTypes = new List<string>();
            var outer = symbol.ContainingType;
            while (outer is not null)
            {
                containingTypes.Insert(0, outer.Name);
                outer = outer.ContainingType;
            }

            return new PartialClassInfo(
                className: symbol.Name,
                namespaceName: containingNamespace,
                configTypeFqn: configTypeFqn,
                isFlags: isFlags,
                alreadyInheritsBase: alreadyInheritsBase,
                containingTypes: containingTypes);
        }

        return null;
    }

    private static string GeneratePartialClassSource(PartialClassInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indentLevel = 0;

        if (info.NamespaceName is not null)
        {
            sb.AppendLine($"namespace {info.NamespaceName}");
            sb.AppendLine("{");
            indentLevel++;
        }

        // Open containing types (for nested classes)
        foreach (var containingType in info.ContainingTypes)
        {
            var outerIndent = new string(' ', indentLevel * 4);
            sb.AppendLine($"{outerIndent}partial class {containingType}");
            sb.AppendLine($"{outerIndent}{{");
            indentLevel++;
        }

        var indent = new string(' ', indentLevel * 4);
        var memberIndent = new string(' ', (indentLevel + 1) * 4);

        var baseClause = "";
        if (!info.AlreadyInheritsBase)
        {
            baseClause = info.IsFlags
                ? " : global::Cocoar.Configuration.Flags.FeatureFlags"
                : " : global::Cocoar.Configuration.Flags.Entitlements";
        }

        sb.AppendLine($"{indent}partial class {info.ClassName}{baseClause}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{memberIndent}private readonly global::Cocoar.Configuration.Reactive.IReactiveConfig<{info.ConfigTypeFqn}> _reactive;");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}protected {info.ConfigTypeFqn} Config => _reactive.CurrentValue;");
        sb.AppendLine();
        sb.AppendLine($"{memberIndent}public {info.ClassName}(global::Cocoar.Configuration.Reactive.IReactiveConfig<{info.ConfigTypeFqn}> reactive)");
        sb.AppendLine($"{memberIndent}{{");
        sb.AppendLine($"{memberIndent}    _reactive = reactive;");
        sb.AppendLine($"{memberIndent}}}");
        sb.AppendLine($"{indent}}}");

        // Close containing types
        for (var i = info.ContainingTypes.Count - 1; i >= 0; i--)
        {
            indentLevel--;
            var outerIndent = new string(' ', indentLevel * 4);
            sb.AppendLine($"{outerIndent}}}");
        }

        if (info.NamespaceName is not null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
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
        public string? Description { get; }

        public MemberInfo(string name, string? description)
        {
            Name = name;
            Description = description;
        }
    }

    private sealed class PartialClassInfo
    {
        public string ClassName { get; }
        public string? NamespaceName { get; }
        public string ConfigTypeFqn { get; }
        public bool IsFlags { get; }
        public bool AlreadyInheritsBase { get; }
        public List<string> ContainingTypes { get; }

        public PartialClassInfo(string className, string? namespaceName, string configTypeFqn, bool isFlags, bool alreadyInheritsBase, List<string> containingTypes)
        {
            ClassName = className;
            NamespaceName = namespaceName;
            ConfigTypeFqn = configTypeFqn;
            IsFlags = isFlags;
            AlreadyInheritsBase = alreadyInheritsBase;
            ContainingTypes = containingTypes;
        }
    }
}
