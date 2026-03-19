using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Generator;

namespace Cocoar.Configuration.Analyzers.Tests;

public class CocoarFlagsGeneratorTests
{
    [Fact]
    public void Generator_WithFlagClass_ProducesDescriptors()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class MyFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Enables new dashboard</summary>
    public FeatureFlag<bool> NewDashboard { get; }

    public MyFlags()
    {
        NewDashboard = () => false;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<MyFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : class;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        Assert.NotNull(generatedSource);
        Assert.Contains("MyFlags", generatedSource);
        Assert.Contains("NewDashboard", generatedSource);
        Assert.Contains("Enables new dashboard", generatedSource);
        Assert.Contains("CocoarFlagsDescriptors", generatedSource);
        Assert.Contains("2099", generatedSource);
    }

    [Fact]
    public void Generator_WithMultipleProperties_ProducesAllDescriptors()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class MultiFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 6, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>First feature flag</summary>
    public FeatureFlag<bool> FeatureAlpha { get; }

    /// <summary>Second feature flag</summary>
    public FeatureFlag<bool> FeatureBeta { get; }

    /// <summary>Third feature flag with int result</summary>
    public FeatureFlag<int> FeatureGamma { get; }

    public MultiFlags()
    {
        FeatureAlpha = () => false;
        FeatureBeta = () => true;
        FeatureGamma = () => 42;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<MultiFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : class;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        Assert.NotNull(generatedSource);
        Assert.Contains("FeatureAlpha", generatedSource);
        Assert.Contains("First feature flag", generatedSource);
        Assert.Contains("FeatureBeta", generatedSource);
        Assert.Contains("Second feature flag", generatedSource);
        Assert.Contains("FeatureGamma", generatedSource);
        Assert.Contains("Third feature flag with int result", generatedSource);
    }

    [Fact]
    public void Generator_ProducesDeterministicOutput()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class DetFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A flag</summary>
    public FeatureFlag<bool> SomeFlag { get; }

    public DetFlags()
    {
        SomeFlag = () => false;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<DetFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : class;
}
";

        var (_, generatedSource1) = RunGenerator(source);
        var (_, generatedSource2) = RunGenerator(source);

        Assert.NotNull(generatedSource1);
        Assert.NotNull(generatedSource2);
        Assert.Equal(generatedSource1, generatedSource2);
    }

    [Fact]
    public void Generator_WithEmptyFlagClass_DoesNotCrash()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class EmptyFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public EmptyFlags() { }
}

public class Registrar
{
    public void Setup(IFlagRegistrar registrar)
    {
        registrar.Register<EmptyFlags>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : class;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        // Empty class has no FeatureFlag<> or Entitlement<> properties, so it won't be detected
        // by the property-based heuristic. But since there's no interface either, it won't produce output.
        // This verifies the generator does not crash.
    }

    [Fact]
    public void Generator_WithEntitlementClass_ProducesEntitlementDescriptors()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class PlanEntitlements
{
    /// <summary>Whether this plan can export data</summary>
    public Entitlement<bool> CanExport { get; }

    /// <summary>Maximum allowed team members</summary>
    public Entitlement<int> MaxUsers { get; }

    public PlanEntitlements()
    {
        CanExport = () => false;
        MaxUsers = () => 5;
    }
}

public class Registrar
{
    public void Setup(IEntRegistrar registrar)
    {
        registrar.Register<PlanEntitlements>();
    }
}

public interface IEntRegistrar
{
    void Register<T>() where T : class;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        Assert.NotNull(generatedSource);
        Assert.Contains("PlanEntitlements", generatedSource);
        Assert.Contains("CanExport", generatedSource);
        Assert.Contains("Whether this plan can export data", generatedSource);
        Assert.Contains("MaxUsers", generatedSource);
        Assert.Contains("Maximum allowed team members", generatedSource);
        Assert.Contains("EntitlementClassDescriptor", generatedSource);
    }

    [Fact]
    public void Generator_WithBothFlagsAndEntitlements_ProducesBothDictionaries()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class MyFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A flag</summary>
    public FeatureFlag<bool> SomeFlag { get; }

    public MyFlags()
    {
        SomeFlag = () => false;
    }
}

public class MyEntitlements
{
    /// <summary>An entitlement</summary>
    public Entitlement<bool> SomeEntitlement { get; }

    public MyEntitlements()
    {
        SomeEntitlement = () => false;
    }
}

public class Registrar
{
    public void Setup(IFlagRegistrar flagReg, IEntRegistrar entReg)
    {
        flagReg.Register<MyFlags>();
        entReg.Register<MyEntitlements>();
    }
}

public interface IFlagRegistrar
{
    void Register<T>() where T : class;
}

public interface IEntRegistrar
{
    void Register<T>() where T : class;
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        Assert.NotNull(generatedSource);
        Assert.Contains("MyFlags", generatedSource);
        Assert.Contains("SomeFlag", generatedSource);
        Assert.Contains("FeatureFlagClassDescriptor", generatedSource);
        Assert.Contains("MyEntitlements", generatedSource);
        Assert.Contains("SomeEntitlement", generatedSource);
        Assert.Contains("EntitlementClassDescriptor", generatedSource);
    }

    [Fact]
    public void Generator_WithNoRegistrations_ProducesNoOutput()
    {
        var source = @"
using System;
using Cocoar.Configuration.Flags;

public class UnregisteredFlags
{
    public DateTimeOffset ExpiresAt => new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public FeatureFlag<bool> SomeFlag { get; }

    public UnregisteredFlags()
    {
        SomeFlag = () => false;
    }
}
";

        var (diagnostics, generatedSource) = RunGenerator(source);

        // No Register<T>() call means no generated output
        Assert.Null(generatedSource);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string? GeneratedSource) RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FeatureFlag<bool>).Assembly.Location),
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
