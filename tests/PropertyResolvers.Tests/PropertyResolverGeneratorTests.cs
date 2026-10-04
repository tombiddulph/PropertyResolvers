using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class PropertyResolverGeneratorTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        return CompilationTestHelper.RunGenerator([source]).Result;
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
        Assert.Contains("global::TestNamespace.Order x => FormatValue(x.AccountId)", generatedCode);
    }

    [Fact]
    public void GeneratorWhenResolverRegistryExistsGeneratesModuleInitializerRegistrations()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId", RegisterRuntime = true)]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var registrationFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("PropertyResolverRegistration.g.cs", StringComparison.Ordinal));

        Assert.NotNull(registrationFile);

        var registrationCode = registrationFile.GetText().ToString();
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", registrationCode);
        Assert.Contains("PropertyResolverRegistry.Register(\"AccountId\"", registrationCode);
        Assert.Contains("global::TestAssembly.AccountIdResolver.__GetString", registrationCode);
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
        Assert.Contains("public static string? GetNonExistentProperty(object? obj)", generatedCode);
        Assert.Contains("_ => null", generatedCode);
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
        Assert.Contains("public static string? GetAccountId(object? obj)", generatedCode);
        Assert.Contains("_ => null", generatedCode);
        // No type-specific match arms since all types are generic
        Assert.DoesNotContain("global::TestNamespace", generatedCode);
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
        Assert.Contains("FormatValue(x.AccountId)", generatedCode);
        Assert.Contains("value?.ToString()", generatedCode);
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
        Assert.Contains("FormatValue(x.Amount)", generatedCode);
        Assert.Contains("value?.ToString()", generatedCode);
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
        Assert.Contains("FormatValue(x.AccountId)", generatedCode);
        Assert.Contains("CultureInfo.InvariantCulture", generatedCode);
    }

    private static List<MetadataReference> GetBaseReferences()
    {
        return CompilationTestHelper.References.ToList();
    }

    private static GeneratorDriverRunResult RunGeneratorWithReferences(string source, params MetadataReference[] additionalReferences)
    {
        return CompilationTestHelper.RunGenerator([source], additionalReferences).Result;
    }

    [Fact]
    public void GeneratorWithAttributeInReferencedAssemblyGeneratesResolver()
    {
        // Step 1: Build a "package" assembly that contains the [assembly: GeneratePropertyResolver] attribute
        const string packageSource = """

                                     using PropertyResolvers.Attributes;

                                     [assembly: GeneratePropertyResolver("AccountId")]
                                     """;

        var packageSyntaxTree = CSharpSyntaxTree.ParseText(packageSource);
        var packageReferences = GetBaseReferences();

        var packageCompilation = CSharpCompilation.Create(
            "PackageAssembly",
            new[] { packageSyntaxTree },
            packageReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var packageErrors = packageCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (packageErrors.Count > 0)
        {
            var errorMessages = string.Join(Environment.NewLine, packageErrors.Select(e => e.ToString()));
            throw new InvalidOperationException(
                $"Package compilation has errors:{Environment.NewLine}{errorMessages}");
        }

        // Create an in-memory reference to the package assembly
        var packageReference = packageCompilation.ToMetadataReference();

        // Step 2: Build the "consuming project" that references the package and has types with AccountId
        const string projectSource = """

                                     namespace MyProject
                                     {
                                         public class Order
                                         {
                                             public string AccountId { get; set; }
                                         }

                                         public class Customer
                                         {
                                             public string AccountId { get; set; }
                                         }
                                     }
                                     """;

        var result = RunGeneratorWithReferences(projectSource, packageReference);

        var generatedFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal));

        Assert.NotNull(generatedFile);

        var generatedCode = generatedFile.GetText().ToString();
        Assert.Contains("public static string? GetAccountId(object? obj)", generatedCode);
        Assert.Contains("global::MyProject.Order x => FormatValue(x.AccountId)", generatedCode);
        Assert.Contains("global::MyProject.Customer x => FormatValue(x.AccountId)", generatedCode);
    }

    [Fact]
    public void GeneratorWithAttributeInReferencedAssemblyAndSourceDeduplicates()
    {
        // Package assembly defines the attribute
        const string packageSource = """

                                     using PropertyResolvers.Attributes;

                                     [assembly: GeneratePropertyResolver("AccountId")]
                                     """;

        var packageSyntaxTree = CSharpSyntaxTree.ParseText(packageSource);
        var packageReferences = GetBaseReferences();

        var packageCompilation = CSharpCompilation.Create(
            "PackageAssembly",
            new[] { packageSyntaxTree },
            packageReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var packageReference = packageCompilation.ToMetadataReference();

        // Consuming project also defines the same attribute AND has types
        const string projectSource = """

                                     using PropertyResolvers.Attributes;

                                     [assembly: GeneratePropertyResolver("AccountId")]

                                     namespace MyProject
                                     {
                                         public class Order
                                         {
                                             public string AccountId { get; set; }
                                         }
                                     }
                                     """;

        var result = RunGeneratorWithReferences(projectSource, packageReference);

        // Should only generate one resolver file (deduplicated)
        var generatedFiles = result.GeneratedTrees
            .Where(t => t.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal))
            .ToList();

        Assert.Single(generatedFiles);

        var generatedCode = generatedFiles[0].GetText().ToString();
        Assert.Contains("global::MyProject.Order x => FormatValue(x.AccountId)", generatedCode);
    }
}
