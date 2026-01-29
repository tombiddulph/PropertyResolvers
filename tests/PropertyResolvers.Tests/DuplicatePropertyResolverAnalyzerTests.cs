using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PropertyResolvers.Generators;
using Xunit;

namespace PropertyResolvers.Tests;

public class DuplicatePropertyResolverAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attributes.GeneratePropertyResolverAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };
        
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new DuplicatePropertyResolverAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
        
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        
        return [..diagnostics.Where(d => d.Id == DuplicatePropertyResolverAnalyzer.DiagnosticId)];
    }

    [Fact]
    public async Task NoDiagnostic_WhenNoAttributes()
    {
        const string source = """

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NoDiagnostic_WhenSingleAttribute()
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NoDiagnostic_WhenDifferentPropertyNames()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("TenantId")]
                              [assembly: GeneratePropertyResolver("EntityId")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                      public string TenantId { get; set; }
                                      public string EntityId { get; set; }
                                  }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Diagnostic_WhenDuplicatePropertyName()
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Single(diagnostics);
        Assert.Equal(DuplicatePropertyResolverAnalyzer.DiagnosticId, diagnostics[0].Id);
        Assert.Contains("AccountId", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task Diagnostic_WhenMultipleDuplicates()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        // Should report 2 diagnostics (second and third occurrences)
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Contains("AccountId", d.GetMessage()));
    }

    [Fact]
    public async Task Diagnostic_WhenCaseInsensitiveDuplicate()
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Single(diagnostics);
        Assert.Contains("accountid", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task Diagnostic_WhenMixedCaseDuplicates()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("ACCOUNTID")]
                              [assembly: GeneratePropertyResolver("accountID")]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);
        
        // Should report 2 diagnostics (ACCOUNTID and accountID)
        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public async Task Diagnostic_OnlyOnDuplicates_NotFirstOccurrence()
    {
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId")]
                              [assembly: GeneratePropertyResolver("TenantId")]
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        // Should report 2 diagnostics (one for AccountId duplicate, one for TenantId duplicate)
        Assert.Equal(2, diagnostics.Length);
        
        var messages = diagnostics.Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("AccountId"));
        Assert.Contains(messages, m => m.Contains("TenantId"));
    }

    [Fact]
    public async Task Diagnostic_WhenDifferentNamespaceOptions_StillDetectsDuplicate()
    {
        // Different namespace options don't make duplicates okay - still same property name
        const string source = """

                              using PropertyResolvers.Attributes;

                              [assembly: GeneratePropertyResolver("AccountId", ExcludeNamespaces = new[] { "System" })]
                              [assembly: GeneratePropertyResolver("AccountId", ExcludeNamespaces = new[] { "Microsoft" })]

                              namespace TestNamespace
                              {
                                  public class Order
                                  {
                                      public string AccountId { get; set; }
                                  }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Single(diagnostics);
        Assert.Contains("AccountId", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task Diagnostic_HasCorrectSeverity()
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async Task Diagnostic_HasCorrectLocation()
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

        var diagnostics = await GetDiagnosticsAsync(source);
        
        Assert.Single(diagnostics);
        
        // The diagnostic should be on line 5 (the duplicate)
        var location = diagnostics[0].Location;
        var lineSpan = location.GetLineSpan();
        Assert.Equal(4, lineSpan.StartLinePosition.Line); // 0-indexed, so line 5 = index 4
    }
}
