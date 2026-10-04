using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PropertyResolvers.Tests.Utils;

internal sealed class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(values);
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    private sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
