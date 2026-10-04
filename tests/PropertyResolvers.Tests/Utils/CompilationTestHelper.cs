using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PropertyResolvers.Attributes;
using PropertyResolvers.Generators;
using Xunit;

namespace PropertyResolvers.Tests.Utils;

internal static class CompilationTestHelper
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp10);

    // Use an explicit framework reference set, independent of test execution order
    // and whichever assemblies happen to have been loaded by the test runner.
    internal static ImmutableArray<MetadataReference> References { get; } = CreateReferences();

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] frameworkAssemblies =
        [
            "System.Private.CoreLib.dll",
            "System.Runtime.dll",
            "netstandard.dll",
            "System.Collections.dll",
            "System.Console.dll",
            "System.Runtime.Extensions.dll",
            "System.Linq.Expressions.dll"
        ];

        return [.. frameworkAssemblies
            .Select(name => MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, name)))
            .Append(MetadataReference.CreateFromFile(typeof(GeneratePropertyResolverAttribute).Assembly.Location))];
    }

    internal static CSharpCompilation CreateCompilation(
        string[] sources,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(
                source, ParseOptions, path: $"Source{index}.cs")),
            References.AddRange(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        return compilation;
    }

    internal static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(
        string[] sources,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CreateCompilation(sources, additionalReferences);
        return RunGenerator(compilation);
    }

    internal static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(
        CSharpCompilation compilation,
        bool allowGeneratorErrors = false,
        AnalyzerConfigOptionsProvider? options = null)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PropertyResolverGenerator().AsSourceGenerator()],
            parseOptions: compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? ParseOptions,
            optionsProvider: options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var result = driver.GetRunResult();

        Assert.All(result.Results, generatorResult => Assert.Null(generatorResult.Exception));
        if (!allowGeneratorErrors)
        {
            AssertNoErrors(diagnostics);
        }
        AssertNoErrors(output.GetDiagnostics());

        return (result, output);
    }

    internal static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    internal static string? InvokeProbe(Compilation compilation, string scenario, IReadOnlyDictionary<string, byte[]>? dependencies = null)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        stream.Position = 0;

        // Isolate module-initializer registration from the test process's registry
        // and from other runtime tests running concurrently.
        var loadContext = new ProbeLoadContext(dependencies);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var probe = assembly.GetType("TestNamespace.Probe", throwOnError: true)!;
            var method = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return (string?)method.Invoke(null, [scenario]);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private sealed class ProbeLoadContext(IReadOnlyDictionary<string, byte[]>? dependencies) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var attributesAssembly = typeof(GeneratePropertyResolverAttribute).Assembly;
            if (assemblyName.Name == attributesAssembly.GetName().Name)
            {
                return LoadFromAssemblyPath(attributesAssembly.Location);
            }
            if (assemblyName.Name is not null && dependencies?.TryGetValue(assemblyName.Name, out var bytes) == true)
            {
                using var stream = new MemoryStream(bytes);
                return LoadFromStream(stream);
            }
            return null;
        }
    }
}
