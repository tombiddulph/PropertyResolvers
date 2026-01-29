using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PropertyResolvers.Generators;

/// <summary>
/// Analyzes assembly-level GeneratePropertyResolver attributes to ensure no duplicate property names are used.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DuplicatePropertyResolverAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PR001";

    private const string AttributeFullName = "PropertyResolvers.Attributes.GeneratePropertyResolverAttribute";

    private static readonly LocalizableString Title =
        "Duplicate property resolver";

    private static readonly LocalizableString MessageFormat =
        "Property resolver for '{0}' is already defined";

    private static readonly LocalizableString Description =
        "Each property name can only have one GeneratePropertyResolver attribute.";

    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Use semantic model to analyze compilation units
        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        var semanticModel = context.SemanticModel;
        var root = semanticModel.SyntaxTree.GetRoot(context.CancellationToken);

        // Find all assembly-level attributes in this syntax tree
        var attributeLists = root.DescendantNodes()
            .OfType<AttributeListSyntax>()
            .Where(al => al.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true);

        var propertyNameToAttributes =
            new Dictionary<string, List<AttributeSyntax>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                // Check if this is our attribute
                var symbolInfo = semanticModel.GetSymbolInfo(attribute, context.CancellationToken);
                if (symbolInfo.Symbol is not IMethodSymbol attributeConstructor)
                {
                    continue;
                }

                var attributeType = attributeConstructor.ContainingType;
                if (attributeType.ToDisplayString() != AttributeFullName)
                {
                    continue;
                }

                // Get the property name from the first argument
                if (attribute.ArgumentList?.Arguments.Count > 0)
                {
                    var firstArg = attribute.ArgumentList.Arguments[0];
                    var constantValue = semanticModel.GetConstantValue(firstArg.Expression, context.CancellationToken);

                    if (constantValue is { HasValue: true, Value: string propertyName })
                    {
                        if (!propertyNameToAttributes.TryGetValue(propertyName, out var list))
                        {
                            list = [];
                            propertyNameToAttributes[propertyName] = list;
                        }

                        list.Add(attribute);
                    }
                }
            }
        }

        // Report diagnostics for duplicates

        for (var i = 0; i < propertyNameToAttributes.Count; i++)
        {
            var kvp = propertyNameToAttributes.ElementAt(i);
            {
                var propertyName = kvp.Key;

                for (var j = 1; j < kvp.Value.Count; j++)
                {
                    var duplicate = kvp.Value[j];
                    var diagnostic = Diagnostic.Create(Rule, duplicate.GetLocation(), propertyName);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}