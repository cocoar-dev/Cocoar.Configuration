using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Generator;

namespace Cocoar.Configuration.Analyzers.Tests;

public class FlagsGeneratorDiagnosticTests
{
    [Fact]
    public void COCFLAG001_NonStaticExpiresAt_EmitsDiagnostic()
    {
        // ExpiresAt uses a method call that can't be statically parsed
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class BadFlags : FeatureFlags
{
    public override DateTimeOffset ExpiresAt => GetExpiry();

    private static DateTimeOffset GetExpiry() => DateTimeOffset.UtcNow.AddYears(1);

    public FeatureFlag<bool> SomeFlag { get; }

    public BadFlags()
    {
        SomeFlag = () => false;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<BadFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : FeatureFlags;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        var cocflag001 = diagnostics.Where(d => d.Id == "COCFLAG001").ToList();
        Assert.NotEmpty(cocflag001);
        Assert.Contains("BadFlags", cocflag001[0].GetMessage());
    }

    [Fact]
    public void COCFLAG002_AbstractTypeRegistered_EmitsDiagnostic()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public abstract class AbstractFlags : FeatureFlags
{
    public override DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public FeatureFlag<bool> SomeFlag { get; }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<AbstractFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : FeatureFlags;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        var cocflag002 = diagnostics.Where(d => d.Id == "COCFLAG002").ToList();
        Assert.NotEmpty(cocflag002);
        Assert.Contains("AbstractFlags", cocflag002[0].GetMessage());
    }

    [Fact]
    public void COCFLAG002_AbstractEntitlementRegistered_EmitsDiagnostic()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public abstract class AbstractEntitlements : Entitlements
{
    public Entitlement<bool> CanExport { get; }
}

public class Registrar
{
    public void Setup(IEntRegistrar registrar)
    {
        registrar.Register<AbstractEntitlements>();
    }
}

public interface IEntRegistrar
{
    void Register<T>() where T : Entitlements;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        var cocflag002 = diagnostics.Where(d => d.Id == "COCFLAG002").ToList();
        Assert.NotEmpty(cocflag002);
        Assert.Contains("AbstractEntitlements", cocflag002[0].GetMessage());
    }

    [Fact]
    public void NoDiagnostic_WhenExpiresAtIsStaticLiteral()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class GoodFlags : FeatureFlags
{
    public override DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 6, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A good flag</summary>
    public FeatureFlag<bool> GoodFlag { get; }

    public GoodFlags()
    {
        GoodFlag = () => false;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<GoodFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : FeatureFlags;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        var flagDiagnostics = diagnostics.Where(d => d.Id.StartsWith("COCFLAG")).ToList();
        Assert.Empty(flagDiagnostics);
        Assert.NotNull(generatedSource);
        Assert.Contains("2099", generatedSource);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string? GeneratedSource) RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FeatureFlags).Assembly.Location),
        };

        // Add runtime assemblies needed for compilation
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new CocoarFlagsGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSource = runResult.GeneratedTrees.FirstOrDefault()?.GetText().ToString();

        return (diagnostics, generatedSource);
    }
}
