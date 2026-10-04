using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PropertyResolvers.Generators;
using PropertyResolvers.Tests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace PropertyResolvers.Tests;

public class IncrementalGeneratorTests(ITestOutputHelper output)
{
    private const string Configuration = """
        [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
        """;
    private const string Entity = "namespace TestNamespace { public class Entity { public int AccountId => 123; } }";
    private const string Unrelated = "public class Unrelated { public int Run() => 1; }";

    private static CSharpGeneratorDriver Driver() => CSharpGeneratorDriver.Create(
        [new PropertyResolverGenerator().AsSourceGenerator()],
        parseOptions: new CSharpParseOptions(LanguageVersion.CSharp10),
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation Edit(CSharpCompilation compilation, int treeIndex, string source)
    {
        var tree = compilation.SyntaxTrees.ElementAt(treeIndex);
        return compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)tree.Options, tree.FilePath));
    }

    private static void AssertCached(GeneratorDriver driver, string step)
    {
        var result = Assert.Single(driver.GetRunResult().Results);
        var reasons = result.TrackedSteps[step].SelectMany(run => run.Outputs).Select(item => item.Reason).ToArray();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"{step} was {reason}"));
    }

    [Fact]
    public void UnrelatedMethodEditsCacheMetadataCandidatesAndGeneratedSources()
    {
        var compilation = CompilationTestHelper.CreateCompilation([Configuration, Entity, Unrelated]);
        var driver = Driver().RunGenerators(compilation);
        driver = driver.RunGenerators(Edit(compilation, 2, Unrelated.Replace("=> 1", "=> 2", StringComparison.Ordinal)));
        AssertCached(driver, "MetadataTypes");
        AssertCached(driver, "SourceTypes");
        AssertCached(driver, "Configuration");
        AssertCached(driver, "ResolverModel");
        AssertCached(driver, "ResolverSources");
    }

    [Fact]
    public void GetterBodyEditsDoNotRegeneratePropertyAccess()
    {
        var compilation = CompilationTestHelper.CreateCompilation([Configuration, Entity]);
        var driver = Driver().RunGenerators(compilation);
        driver = driver.RunGenerators(Edit(compilation, 1, Entity.Replace("123", "456", StringComparison.Ordinal)));
        AssertCached(driver, "MetadataTypes");
        AssertCached(driver, "ResolverModel");
        AssertCached(driver, "ResolverSources");
    }

    [Fact]
    public void PropertyChangesInvalidateOnlyRelevantGeneration()
    {
        var compilation = CompilationTestHelper.CreateCompilation([Configuration, Entity]);
        var driver = Driver().RunGenerators(compilation);
        driver = driver.RunGenerators(Edit(compilation, 1, Entity.Replace("AccountId", "OtherId", StringComparison.Ordinal)));
        AssertCached(driver, "MetadataTypes");
        var result = driver.GetRunResult();
        var resolver = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("Entity x =>", resolver.GetText().ToString());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "PR002");
    }

    [Fact]
    public void ReferenceAndConfigurationChangesInvalidateMetadataDiscovery()
    {
        var compilation = CompilationTestHelper.CreateCompilation([Configuration, Entity]);
        var driver = Driver().RunGenerators(compilation);
        var external = CompilationTestHelper.CreateCompilation([
            "namespace TestNamespace { public class External { public int AccountId => 456; } }"
        ]).WithAssemblyName("ExternalAssembly");
        driver = driver.RunGenerators(compilation.AddReferences(external.ToMetadataReference()));
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.GetText().ToString().Contains("External x =>", StringComparison.Ordinal));

        var changed = Edit(compilation, 0, Configuration.Replace("AccountId", "OtherId", StringComparison.Ordinal));
        driver = driver.RunGenerators(changed);
        Assert.Contains(driver.GetRunResult().GeneratedTrees, tree => tree.FilePath.EndsWith("OtherIdResolver.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.GetRunResult().GeneratedTrees, tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void PartialTypesHaveOnePatternWithMembersFromAllDeclarations()
    {
        var (result, _) = CompilationTestHelper.RunGenerator([
            Configuration,
            "namespace TestNamespace { public partial class Entity { } }",
            "namespace TestNamespace { public partial class Entity { public int AccountId => 123; } }"
        ]);
        Assert.Empty(result.Diagnostics);
        var resolver = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(1, resolver.Split("Entity x =>", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CachedDiagnosticModelsUseCurrentAttributeLocations()
    {
        var configuration = Configuration.Replace("AccountId", "Missing", StringComparison.Ordinal);
        var compilation = CompilationTestHelper.CreateCompilation([configuration, Entity]);
        var driver = Driver().RunGenerators(compilation);
        var edited = Edit(compilation, 0, "\n\n" + configuration);
        driver = driver.RunGenerators(edited);
        AssertCached(driver, "ResolverModel");
        var diagnostic = Assert.Single(driver.GetRunResult().Diagnostics);
        Assert.Equal("PR002", diagnostic.Id);
        Assert.Same(edited.SyntaxTrees.First(), diagnostic.Location.SourceTree);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void LargeSyntheticProjectMeasuresColdUnrelatedAndRelevantEdits()
    {
        var entities = string.Join(Environment.NewLine, Enumerable.Range(0, 1000).Select(index =>
            "public class Entity" + index.ToString(CultureInfo.InvariantCulture) + " { public int AccountId => 123; }"));
        var source = "namespace TestNamespace { " + entities + " }";
        var compilation = CompilationTestHelper.CreateCompilation([Configuration, source, Unrelated]);
        var stopwatch = Stopwatch.StartNew();
        var driver = Driver().RunGenerators(compilation);
        output.WriteLine("Cold generation (1,000 types): {0} ms", stopwatch.ElapsedMilliseconds);
        stopwatch.Restart();
        driver = driver.RunGenerators(Edit(compilation, 2, Unrelated.Replace("=> 1", "=> 2", StringComparison.Ordinal)));
        output.WriteLine("Unrelated edit: {0} ms", stopwatch.ElapsedMilliseconds);
        AssertCached(driver, "MetadataTypes");
        AssertCached(driver, "ResolverModel");
        AssertCached(driver, "ResolverSources");
        stopwatch.Restart();
        driver = driver.RunGenerators(Edit(compilation, 1, source.Replace("public int AccountId => 123;", "public int? AccountId => null;", StringComparison.Ordinal)));
        output.WriteLine("Relevant property edits: {0} ms", stopwatch.ElapsedMilliseconds);
        var result = driver.GetRunResult();
        Assert.All(result.Results, generator => Assert.Null(generator.Exception));
        Assert.Empty(result.Diagnostics);
    }
}
