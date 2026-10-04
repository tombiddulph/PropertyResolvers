using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PropertyResolvers.CodeFixes;
using PropertyResolvers.Generators;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class DuplicatePropertyResolverCodeFixTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateFixPreservesOtherAttributesAndCode(bool sharedList)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "Test", "Test", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp10),
            metadataReferences: CompilationTestHelper.References));
        var declarations = sharedList
            ? "[assembly: GeneratePropertyResolver(\"AccountId\"), GeneratePropertyResolver(\"accountid\"), System.CLSCompliant(true)]"
            : "[assembly: GeneratePropertyResolver(\"AccountId\")]\n[assembly: GeneratePropertyResolver(\"accountid\")]\n[assembly: System.CLSCompliant(true)]";
        var document = workspace.AddDocument(project.Id, "Source.cs", SourceText.From(
            "using PropertyResolvers.Attributes;\n" + declarations + "\npublic class Entity { public string AccountId => \"value\"; }"));
        var compilation = (await document.Project.GetCompilationAsync(CancellationToken.None))!;
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicatePropertyResolverAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        var diagnostic = Assert.Single(diagnostics);
        var actions = new List<CodeAction>();
        var provider = new DuplicatePropertyResolverCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));
        var action = Assert.Single(actions);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changes = Assert.Single(operations.OfType<ApplyChangesOperation>());
        var updated = changes.ChangedSolution.GetDocument(document.Id)!;
        var root = (await updated.GetSyntaxRootAsync(CancellationToken.None))!;
        Assert.Equal(2, root.DescendantNodes().OfType<AttributeSyntax>().Count());
        Assert.Contains("CLSCompliant", root.ToFullString());
        Assert.Contains("public class Entity", root.ToFullString());
        CompilationTestHelper.AssertNoErrors((await updated.Project.GetCompilationAsync(CancellationToken.None))!.GetDiagnostics());
        Assert.NotNull(provider.GetFixAllProvider());
    }
}
