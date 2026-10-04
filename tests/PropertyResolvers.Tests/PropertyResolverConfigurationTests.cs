using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class PropertyResolverConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1AccountId")]
    [InlineData("Account-Id")]
    [InlineData("Account Id")]
    [InlineData("Customer..AccountId")]
    [InlineData("@event")]
    [InlineData("AccountId\n")]
    [InlineData("AccountId\"}")]
    [InlineData("Account\\u0049d")]
    [InlineData("Account\u200dId")]
    public void InvalidPropertyNamesProduceDiagnosticInsteadOfGeneratedCode(string propertyName)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver({{Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(propertyName, quote: true)}})]
                       """;
        var compilation = CompilationTestHelper.CreateCompilation([source]);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PR004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Source0.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void NullPropertyNameProducesDiagnostic()
    {
        var compilation = CompilationTestHelper.CreateCompilation([
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(null)]"
        ]);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR004", Assert.Single(result.Diagnostics).Id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void InvalidConfigurationDoesNotSuppressValidResolversOrRegisterInvalidOnes()
    {
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("Customer..AccountId")]
            [assembly: GeneratePropertyResolver("AccountId", RegisterRuntime = true)]
            namespace TestNamespace
            {
                public class Entity { public string AccountId => "valid"; }
                public static class Probe
                {
                    public static string? Run(string scenario) =>
                        PropertyResolverRegistry.TryResolve("AccountId", new Entity(), out var value) ? value : "missing";
                }
            }
            """]);

        var (result, output) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR004", Assert.Single(result.Diagnostics).Id);
        Assert.Equal(3, result.GeneratedTrees.Length);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("Customer..AccountId", StringComparison.Ordinal));
        Assert.Equal("valid", CompilationTestHelper.InvokeProbe(output, "registry"));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("record")]
    [InlineData("ΔId")]
    [InlineData("café")]
    [InlineData("_")]
    public void ValidKeywordsAndUnicodeIdentifiersGenerateCompilableResolvers(string propertyName)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("{{propertyName}}", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace
                       {
                           public class Entity { public string @{{propertyName}} => "valid"; }
                       }
                       """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.EndsWith($"{propertyName}Resolver.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("My-App")]
    [InlineData("My App")]
    [InlineData("My..App")]
    [InlineData(".MyApp")]
    [InlineData("MyApp.")]
    [InlineData("1MyApp")]
    [InlineData("MyApp.@class")]
    public void InvalidAssemblyNamespacesProduceDiagnostic(string assemblyName)
    {
        var compilation = CompilationTestHelper.CreateCompilation([
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\")]"
        ]).WithAssemblyName(assemblyName);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PR007", diagnostic.Id);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void KeywordNamespaceSegmentsAreEscapedIncludingRegistration()
    {
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" }, RegisterRuntime = true)]
            namespace TestNamespace
            {
                public class Entity { public string AccountId => "escaped-namespace"; }
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        if (scenario == "registry")
                        {
                            return PropertyResolverRegistry.TryResolve("AccountId", new Entity(), out var value) ? value : "missing";
                        }
                        return global::@class.@namespace.AccountIdResolver.GetAccountId(new Entity());
                    }
                }
            }
            """]).WithAssemblyName("class.namespace");

        var (result, output) = CompilationTestHelper.RunGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        Assert.All(result.GeneratedTrees, tree => Assert.Contains("namespace @class.@namespace;", tree.GetText().ToString()));
        Assert.Equal("escaped-namespace", CompilationTestHelper.InvokeProbe(output, "direct"));
        Assert.Equal("escaped-namespace", CompilationTestHelper.InvokeProbe(output, "registry"));
    }

    [Theory]
    [InlineData("public class AccountIdResolver { }")]
    [InlineData("public partial class AccountIdResolver { }")]
    [InlineData("public struct AccountIdResolver { }")]
    [InlineData("public interface AccountIdResolver { }")]
    [InlineData("public enum AccountIdResolver { Value }")]
    [InlineData("public delegate void AccountIdResolver();")]
    [InlineData("namespace AccountIdResolver { public class Child { } }")]
    public void ResolverNameCollisionsSkipOnlyConflictingResolvers(string declaration)
    {
        var compilation = CompilationTestHelper.CreateCompilation([
            """
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId")]
            [assembly: GeneratePropertyResolver("TenantId")]
            """,
            $"namespace TestAssembly {{ {declaration} }}"
        ]);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal("PR008", diagnostic.Id);
        Assert.Contains("TestAssembly.AccountIdResolver", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal("Source0.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.EndsWith("TenantIdResolver.g.cs", StringComparison.Ordinal));
        var registration = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("PropertyResolverDispatch.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("AccountId", registration.GetText().ToString());
    }

    [Theory]
    [InlineData("public class PropertyResolverModuleInitializer { }")]
    [InlineData("namespace PropertyResolverModuleInitializer { }")]
    public void InitializerNameCollisionsDoNotSuppressDirectResolvers(string declaration)
    {
        var compilation = CompilationTestHelper.CreateCompilation([
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\", RegisterRuntime = true)]",
            $"namespace TestAssembly {{ {declaration} }}"
        ]);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal("PR008", diagnostic.Id);
        Assert.Contains("TestAssembly.PropertyResolverModuleInitializer", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(2, result.GeneratedTrees.Length);
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.FilePath.EndsWith("PropertyResolverRegistration.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TestAssembly", "public class TestAssembly { }")]
    [InlineData("TestAssembly.Generated", "public class TestAssembly { }")]
    [InlineData("TestAssembly.Generated", "namespace TestAssembly { public class Generated { } }")]
    public void TypesBlockingNamespaceSegmentsProduceCollisionDiagnostic(string assemblyName, string declaration)
    {
        var compilation = CompilationTestHelper.CreateCompilation([
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\")]",
            declaration
        ]).WithAssemblyName(assemblyName);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR008", Assert.Single(result.Diagnostics).Id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void GenericTypesAndOtherNamespacesDoNotCauseFalseCollisions()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId")]
                              public class TestAssembly<T> { }
                              namespace TestAssembly
                              {
                                  public class AccountIdResolver<T> { }
                                  public class PropertyResolverModuleInitializer<T> { }
                              }
                              namespace OtherNamespace
                              {
                                  public class AccountIdResolver { }
                                  public class PropertyResolverModuleInitializer { }
                              }
                              """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("PR002", Assert.Single(result.Diagnostics).Id);
        Assert.Equal(2, result.GeneratedTrees.Length);
    }

    [Fact]
    public void InvalidNamespaceWithoutResolverConfigurationProducesNoDiagnostic()
    {
        var compilation = CompilationTestHelper.CreateCompilation(["public class Entity { }"])
            .WithAssemblyName("My-App");

        var (result, _) = CompilationTestHelper.RunGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void NullNamespaceFiltersRemainValidConfiguration()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = null, ExcludeNamespaces = null)]
                              namespace TestNamespace { public class Entity { public string AccountId => "valid"; } }
                              """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("x.AccountId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidReferencedConfigurationReportsWithoutSourceLocation(bool useCompilationReference)
    {
        var referencedCompilation = CompilationTestHelper.CreateCompilation([
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"Customer..AccountId\")]"
        ]).WithAssemblyName("ReferencedConfiguration");
        using var stream = new MemoryStream();
        Assert.True(referencedCompilation.Emit(stream).Success);
        MetadataReference reference = useCompilationReference
            ? referencedCompilation.ToMetadataReference()
            : MetadataReference.CreateFromImage(stream.ToArray());
        var compilation = CompilationTestHelper.CreateCompilation(["public class Entity { }"], reference);

        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("PR004", diagnostic.Id);
        Assert.Equal(Location.None, diagnostic.Location);
        Assert.Empty(result.GeneratedTrees);
    }
}
