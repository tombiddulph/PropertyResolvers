using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PropertyResolvers.CodeFixes;

/// <summary>Removes only the redundant configuration identified by PR001.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DuplicatePropertyResolverCodeFixProvider)), Shared]
public sealed class DuplicatePropertyResolverCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["PR001"];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var attribute = root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute is null)
        {
            return;
        }
        context.RegisterCodeFix(CodeAction.Create("Remove duplicate property resolver",
            token => RemoveAsync(context.Document, attribute, token),
            equivalenceKey: nameof(DuplicatePropertyResolverCodeFixProvider)), context.Diagnostics.First());
    }

    private static async Task<Document> RemoveAsync(Document document, AttributeSyntax attribute, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var list = (AttributeListSyntax)attribute.Parent!;
        SyntaxNode removed = list.Attributes.Count == 1 ? list : attribute;
        var updated = root!.RemoveNode(removed, SyntaxRemoveOptions.KeepExteriorTrivia)!;
        return document.WithSyntaxRoot(updated);
    }
}
