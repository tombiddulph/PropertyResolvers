using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using PropertyResolvers.Generators;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class DuplicatePropertyResolverAnalyzerTests
{
    [Fact]
    public async Task DiagnosticWhenDuplicatesAreInDifferentFiles()
    {
        var diagnostics = await GetDiagnosticsAsync(
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\")]",
            "[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"accountid\")]");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Source1.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Contains("AccountId", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task NamedConstructorArgumentIsRecognized()
    {
        var diagnostics = await GetDiagnosticsAsync("""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId")]
            [assembly: GeneratePropertyResolver(propertyName: "AccountId", IncludeNamespaces = new[] { "Example" })]
            """);

        Assert.Single(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var compilation = CompilationTestHelper.CreateCompilation(sources);
        CompilationTestHelper.AssertNoErrors(compilation.GetDiagnostics());

        var analyzer = new DuplicatePropertyResolverAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return [.. diagnostics.Where(d => d.Id == DuplicatePropertyResolverAnalyzer.DiagnosticId)];
    }

    [Fact]
    public async Task NoDiagnosticWhenNoAttributes()
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
    public async Task NoDiagnosticWhenSingleAttribute()
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
    public async Task NoDiagnosticWhenDifferentPropertyNames()
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
    public async Task DiagnosticWhenDuplicatePropertyName()
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
        Assert.Contains("AccountId", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DiagnosticWhenMultipleDuplicates()
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
        Assert.All(diagnostics, d => Assert.Contains("AccountId", d.GetMessage(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task DiagnosticWhenCaseInsensitiveDuplicate()
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
        // The message contains the first-seen property name (case-insensitive match)
        Assert.Contains("AccountId", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DiagnosticWhenMixedCaseDuplicates()
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
    public async Task DiagnosticOnlyOnDuplicatesNotFirstOccurrence()
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

        var messages = diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();
        Assert.Contains(messages, m => m.Contains("AccountId"));
        Assert.Contains(messages, m => m.Contains("TenantId"));
    }

    [Fact]
    public async Task DiagnosticWhenDifferentNamespaceOptionsStillDetectsDuplicate()
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
        Assert.Contains("AccountId", diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DiagnosticHasCorrectSeverity()
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
    public async Task DiagnosticHasCorrectLocation()
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
