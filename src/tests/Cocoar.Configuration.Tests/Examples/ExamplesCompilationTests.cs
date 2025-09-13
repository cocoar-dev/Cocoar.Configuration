using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cocoar.Configuration.Tests.Examples;

public class ExamplesCompilationTests
{
    [Fact]
    public void All_Example_Files_Compile()
    {
        var examplesDir = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "Examples"));
        Assert.True(Directory.Exists(examplesDir), $"Examples directory not found: {examplesDir}");

        var exampleFiles = Directory.GetFiles(examplesDir, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(exampleFiles);

        // Basic references: current test assembly refs + framework assemblies
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var failures = new List<string>();
        foreach (var file in exampleFiles)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, path: file);
            var compilation = CSharpCompilation.Create(
                assemblyName: Path.GetFileNameWithoutExtension(file) + "_ExampleCheck",
                syntaxTrees: new[] { tree },
                references: refs,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));

            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);
            if (!emit.Success)
            {
                var diagnostics = string.Join('\n', emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{Path.GetFileName(file)}: {d.Id} {d.GetMessage()}"));
                failures.Add(diagnostics);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail("Example compilation failures:\n" + string.Join("\n\n", failures));
        }
    }
}
