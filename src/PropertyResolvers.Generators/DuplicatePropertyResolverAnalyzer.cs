using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
        description: Description,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var propertyNames = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in context.Compilation.Assembly.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() != AttributeFullName ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string propertyName)
            {
                continue;
            }

            // Referenced assembly configuration remains an inherited default, not
            // a duplicate declaration in the consuming assembly.
            if (propertyNames.TryGetValue(propertyName, out var firstPropertyName))
            {
                var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken);
                if (syntax is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.GetLocation(), firstPropertyName));
                }
            }
            else
            {
                propertyNames.Add(propertyName, propertyName);
            }
        }
    }
}
