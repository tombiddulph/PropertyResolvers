using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PropertyResolvers.Generators;
using Xunit;

namespace PropertyResolvers.Tests;

public class PropertyResolverGeneratorTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        // Create syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Include all currently loaded assemblies to ensure proper symbol resolution
        // This mimics how the IDE has access to all project references
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Ensure our attribute assembly is included
        var attributeAssemblyLocation = typeof(Attributes.GeneratePropertyResolverAttribute).Assembly.Location;
        if (references.All(r => r.Display != attributeAssemblyLocation))
        {
            references.Add(MetadataReference.CreateFromFile(attributeAssemblyLocation));
        }

        // Create compilation
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Verify compilation has no errors - if it does, symbol resolution won't work properly
        var compilationErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (compilationErrors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, compilationErrors.Select(e => e.ToString()));
            throw new InvalidOperationException(
                $"Test compilation has errors. Symbol resolution will not work correctly:{Environment.NewLine}{errorMessages}");
        }

        // Create and run generator
        var generator = new PropertyResolverGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation).GetRunResult();
    }

    [Fact]
    public void GeneratorWithSinglePropertyGeneratesResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Check for generated file
        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("public static string? GetAccountId(object? obj)", generatedCode);
        Assert.Contains("global::TestNamespace.Order x => x.AccountId", generatedCode);
    }

    [Fact]
    public void GeneratorWithMultiplePropertiesGeneratesMultipleResolvers()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("TenantId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                      public string TenantId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        Assert.Contains(result.GeneratedTrees,
            t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedTrees,
            t => t.FilePath.EndsWith("TenantIdResolver.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorWithExcludeNamespacesExcludesMatchingTypes()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId", ExcludeNamespaces = new[] { "Excluded" })]

                              namespace Included
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }

                              namespace Excluded
                              {
                                  public class Customer
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("global::Included.Order", generatedCode);
        Assert.DoesNotContain("global::Excluded.Customer", generatedCode);
    }

    [Fact]
    public void GeneratorWithIncludeNamespacesOnlyIncludesMatchingTypes()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "Included" })]

                              namespace Included
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }

                              namespace Other
                              {
                                  public class Customer
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("global::Included.Order", generatedCode);
        Assert.DoesNotContain("global::Other.Customer", generatedCode);
    }

    [Fact]
    public void GeneratorWithNoMatchingTypesGeneratesEmptyResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("NonExistentProperty")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("NonExistentPropertyResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("public static class NonExistentPropertyResolver", generatedCode);
        Assert.DoesNotContain("public static string?", generatedCode);
    }

    [Fact]
    public void GeneratorWithMultipleMatchingTypesIncludesAllInResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                                  
                                  public class Customer
                                  {
                                      public string AccountId { get; set; }
                                  }
                                  
                                  public class Invoice
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("global::TestNamespace.Order", generatedCode);
        Assert.Contains("global::TestNamespace.Customer", generatedCode);
        Assert.Contains("global::TestNamespace.Invoice", generatedCode);
    }

    [Fact]
    public void GeneratorWithDuplicatePropertyNamesDeduplicatesAndGeneratesSingleResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should only generate one resolver file, not two
        var generatedFiles = result.GeneratedTrees
            .Where(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal))
            .ToList();

        Assert.Single(generatedFiles);
    }

    [Fact]
    public void GeneratorWithCaseInsensitiveDuplicatesDeduplicatesCorrectly()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("accountid")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should only generate one resolver file (takes the first one)
        var accountIdFiles = result.GeneratedTrees
            .Where(t => t.FilePath.Contains("Resolver.g.cs"))
            .ToList();

        Assert.Single(accountIdFiles);
    }

    [Fact]
    public void GeneratorUsesAssemblyNameForNamespace()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("namespace TestAssembly;", generatedCode);
    }

    [Fact]
    public void GeneratorWithStructTypeIncludesInResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public struct OrderStruct
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("global::TestNamespace.OrderStruct", generatedCode);
    }

    [Fact]
    public void GeneratorWithGenericTypeSkipsGenericType()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class GenericEntity<T>
                                  {
                                      public string AccountId { get; set; }
                                      public T Value { get; set; }
                                  }
                                  
                                  public class NonGenericEntity
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();

        // Should include the non-generic type
        Assert.Contains("global::TestNamespace.NonGenericEntity", generatedCode);

        // Should NOT include the generic type in the switch expression
        Assert.DoesNotContain("global::TestNamespace.GenericEntity", generatedCode);
    }

    [Fact]
    public void GeneratorWithOnlyGenericTypesGeneratesEmptyResolver()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class GenericEntity<T>
                                  {
                                      public string AccountId { get; set; }
                                  }
                                  
                                  public class AnotherGeneric<TKey, TValue>
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("public static class AccountIdResolver", generatedCode);
        Assert.DoesNotContain("public static string?", generatedCode);
    }

    [Fact]
    public void GeneratorWithNullableReferenceTypeUsesNullConditional()
    {
        const string source = """

                              #nullable enable
                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string? AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("x.AccountId?.ToString()", generatedCode);
    }

    [Fact]
    public void GeneratorWithNullableValueTypeUsesNullConditional()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("Amount")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public int? Amount { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AmountResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("x.Amount?.ToString()", generatedCode);
    }

    [Fact]
    public void GeneratorWithNonNullableTypeUsesToString()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("x.AccountId.ToString()", generatedCode);
        Assert.DoesNotContain("x.AccountId?.ToString()", generatedCode);
    }
}