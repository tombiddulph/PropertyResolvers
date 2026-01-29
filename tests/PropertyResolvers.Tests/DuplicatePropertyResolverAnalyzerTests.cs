using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
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

        // Include all currently loaded assemblies to ensure proper symbol resolution
        // This mimics how the IDE has access to all project references
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Ensure our attribute assembly is included
        var attributeAssemblyLocation = typeof(Attributes.GeneratePropertyResolverAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == attributeAssemblyLocation))
        {
            references.Add(MetadataReference.CreateFromFile(attributeAssemblyLocation));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
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

        var analyzer = new DuplicatePropertyResolverAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

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
