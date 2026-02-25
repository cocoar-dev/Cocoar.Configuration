using System;
using System.IO;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Cocoar.Configuration.Analyzers.Analyzers;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.DI;


namespace Cocoar.Configuration.Analyzers.Tests;

public class RuleOrderingAnalyzerTests
{
    [Fact]
    public async Task NoDiagnostic_WhenRulesInCorrectOrder()
    {
    var source = @"
using Cocoar.Configuration;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    void Configure()
    {
        var builder = new ServiceCollection();
        builder.AddCocoarConfiguration(c => c.WithConfiguration(rule => [
            rule.For<AppSettings>().FromFile(""app.json""),
            rule.For<DerivedConfig>()
                .FromFile(""derived.json"")
                .When(accessor => accessor.GetRequiredConfig<AppSettings>().IsEnabled)
        ]));
    }
}

public class AppSettings 
{ 
    public bool IsEnabled { get; set; }
}

public class DerivedConfig 
{
}
";

        var diagnostics = await GetDiagnosticsAsync(source, new RuleOrderingAnalyzer());
    Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Diagnostic_WhenRuleDependsOnLaterType()
    {
    var source = @"
using Cocoar.Configuration;
using Cocoar.Configuration.DI;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    void Configure()
    {
        var builder = new ServiceCollection();
        builder.AddCocoarConfiguration(c => c.WithConfiguration(rule => [
            rule.For<DerivedConfig>()
                .FromFile(""derived.json"")
                .When(accessor => accessor.GetRequiredConfig<AppSettings>().IsEnabled),
            rule.For<AppSettings>().FromFile(""app.json"")
        ]));
    }
}

public class AppSettings 
{ 
    public bool IsEnabled { get; set; }
}

public class DerivedConfig 
{
}
";

        var diagnostics = await GetDiagnosticsAsync(source, new RuleOrderingAnalyzer());

        var ruleDiag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticDescriptors.RuleOrderingViolation.Id, ruleDiag.Id);
        Assert.Contains("DerivedConfig", ruleDiag.GetMessage());
        Assert.Contains("AppSettings", ruleDiag.GetMessage());
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
    var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
    var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location));
    references.Add(MetadataReference.CreateFromFile(typeof(ILogger).Assembly.Location));
        // Include Cocoar.Configuration assembly (extension methods & rule types) using a known type's assembly location.
    references.Add(MetadataReference.CreateFromFile(typeof(ConfigRule).Assembly.Location));
    references.Add(MetadataReference.CreateFromFile(typeof(CocoarConfigurationExtensions).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references.ToImmutable(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer), new CompilationWithAnalyzersOptions(
            analyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
