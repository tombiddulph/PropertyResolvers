using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PropertyResolvers.Attributes;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class StructuralResolverTests
{
    [Theory]
    [InlineData("int", "123", "123")]
    [InlineData("int?", "null", "null")]
    [InlineData("string", "\"abc\"", "abc")]
    [InlineData("string?", "null", "null")]
    [InlineData("System.Guid", "new System.Guid(\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\")", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    public void TypedTryGetPreservesTypesAndDistinguishesMissingFromNull(string type, string initializer, string expected)
    {
        var source = $$"""
                       #nullable enable
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.Typed, IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace
                       {
                           public class Entity { public {{type}} AccountId => {{initializer}}; }
                           public static class Probe
                           {
                               public static string? Run(string scenario)
                               {
                                   object? source = scenario == "value" ? new Entity() : scenario == "unknown" ? new object() : null;
                                   var found = TestAssembly.AccountIdResolver.TryGet(source, out var value);
                                   return (found ? "found:" : "missing:") + (value?.ToString() ?? "null");
                               }
                           }
                       }
                       """;
        // Non-nullable value-type TryGet deliberately has a non-nullable out value.
        if (type is "int" or "System.Guid")
        {
            source = source.Replace("value?.ToString()", "TestAssembly.AccountIdResolver.GetAccountId(source)?.ToString()", StringComparison.Ordinal);
        }
        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("found:" + expected, CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Equal("missing:null", CompilationTestHelper.InvokeProbe(output, "unknown"));
        Assert.Equal("missing:null", CompilationTestHelper.InvokeProbe(output, "null"));
    }

    [Fact]
    public void NullableAndNonNullableValuePropertiesShareNullableTypedOutParameter()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.Typed, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class A { public int AccountId => 123; }
                public class B { public int? AccountId => null; }
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        var found = TestAssembly.AccountIdResolver.TryGet(scenario == "null" ? (object)new B() : new A(), out int? value);
                        return (found ? "found:" : "missing:") + (value?.ToString() ?? "null");
                    }
                }
            }
            """]);
        Assert.Equal("found:123", CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Equal("found:null", CompilationTestHelper.InvokeProbe(output, "null"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(", Output = ResolverOutput.Typed", true)]
    public void MixedPropertyTypesAreValidOnlyForStringOutput(string option, bool error)
    {
        var compilation = CompilationTestHelper.CreateCompilation([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" }{{option}})]
            namespace TestNamespace
            {
                public class A { public int AccountId => 123; }
                public class B { public string AccountId => "abc"; }
            }
            """]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: error);
        if (error)
        {
            Assert.Equal("PR003", Assert.Single(result.Diagnostics).Id);
            Assert.Empty(result.GeneratedTrees);
        }
        else
        {
            Assert.Empty(result.Diagnostics);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticDispatchWorksWithoutModuleInitialization(bool typed)
    {
        var (result, output) = CompilationTestHelper.RunGenerator([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.{{(typed ? "Typed" : "String")}}, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public int? AccountId => null; }
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        var name = scenario == "unknown-name" ? "Other" : "accountid";
                        object? source = scenario == "unknown-type" ? new object() : scenario == "null-source" ? null : new Entity();
                        var found = TestAssembly.PropertyResolverDispatch.TryResolve(name, source, out var value);
                        return (found ? "found:" : "missing:") + (value ?? "null");
                    }
                }
            }
            """]);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("ModuleInitializer", StringComparison.Ordinal));
        Assert.Equal("found:null", CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Equal("missing:null", CompilationTestHelper.InvokeProbe(output, "unknown-name"));
        Assert.Equal("missing:null", CompilationTestHelper.InvokeProbe(output, "unknown-type"));
        Assert.Equal("missing:null", CompilationTestHelper.InvokeProbe(output, "null-source"));
    }

    [Fact]
    public void AliasesAndCustomNamesResolveAcrossDifferentShapes()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Aliases = new[] { "AccountID", "AccountNumber" },
                Namespace = "Custom.Generated", ResolverName = "AccountResolver", Output = ResolverOutput.Typed,
                IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class A { public int AccountId => 123; }
                public class B { public int AccountNumber => 456; }
                public static class Probe
                {
                    public static string? Run(string scenario) => Custom.Generated.AccountResolver.GetAccountId(
                        scenario == "legacy" ? (object)new B() : new A())?.ToString();
                }
            }
            """]);
        Assert.Equal("123", CompilationTestHelper.InvokeProbe(output, "modern"));
        Assert.Equal("456", CompilationTestHelper.InvokeProbe(output, "legacy"));
    }

    [Theory]
    [InlineData("public int AccountId => 1; public int AccountID => 2;", "")]
    [InlineData("public int AccountId => 1; public int AccountNumber => 2;", ", Aliases = new[] { \"AccountNumber\" }")]
    public void AmbiguousPropertiesAndAliasesProduceErrors(string properties, string options)
    {
        var compilation = CompilationTestHelper.CreateCompilation([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" }{{options}})]
            namespace TestNamespace { public class Entity { {{properties}} } }
            """]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR009", Assert.Single(result.Diagnostics).Id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void CaseSensitiveMatchingResolvesCaseAmbiguity()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", CaseSensitive = true, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public int AccountId => 1; public int AccountID => 2; }
                public static class Probe { public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Entity()); }
            }
            """]);
        Assert.Equal("1", CompilationTestHelper.InvokeProbe(output, "value"));
    }

    [Theory]
    [InlineData("class", "Child?", "new Child { AccountId = 123 }")]
    [InlineData("struct", "Child?", "new Child { AccountId = 123 }")]
    [InlineData("struct", "Child", "new Child { AccountId = 123 }")]
    public void NestedPathsPropagateNullableIntermediates(string childKind, string childType, string initializer)
    {
        var source = $$"""
            #nullable enable
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("Customer.AccountId", Output = ResolverOutput.Typed, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public {{childKind}} Child { public int AccountId { get; set; } }
                public class Entity { public {{childType}} Customer { get; set; } }
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        var source = scenario == "value" ? new Entity { Customer = {{initializer}} } : new Entity();
                        var found = TestAssembly.CustomerAccountIdResolver.TryGet(source, out var value);
                        return (found ? "found:" : "missing:") + TestAssembly.CustomerAccountIdResolver.GetCustomerAccountId(source)?.ToString();
                    }
                }
            }
            """;
        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("found:123", CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Equal(childType == "Child" ? "found:0" : "found:", CompilationTestHelper.InvokeProbe(output, "null"));
    }

    [Fact]
    public void NestedPathsAndAliasesCanUseInheritedInterfaceProperties()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            #nullable enable
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("Customer.AccountId", Aliases = new[] { "Legacy.TenantId" }, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public interface IBase { string AccountId { get; } }
                public interface ICustomer : IBase { }
                public class Customer : ICustomer { public string AccountId => "nested"; }
                public class Entity { public ICustomer? Customer => new Customer(); }
                public static class Probe { public static string? Run(string scenario) => TestAssembly.CustomerAccountIdResolver.GetCustomerAccountId(new Entity()); }
            }
            """]);
        Assert.Equal("nested", CompilationTestHelper.InvokeProbe(output, "value"));
    }

    [Theory]
    [InlineData("", "PR002")]
    [InlineData(", ExcludeNamespaces = new[] { \"TestNamespace\" }", "PR006")]
    public void NoMatchDiagnosticsExplainMissingAndExcludedMatches(string options, string id)
    {
        var property = id == "PR002" ? "MissingProperty" : "AccountId";
        var (result, _) = CompilationTestHelper.RunGenerator([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("{{property}}"{{options}})]
            namespace TestNamespace { public class Entity { public int AccountId => 1; } }
            """]);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Theory]
    [InlineData("Customer.Missing")]
    [InlineData("Customer.AccountId")]
    public void UnsupportedPathsHaveUsefulDiagnostics(string path)
    {
        var (result, _) = CompilationTestHelper.RunGenerator([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("{{path}}", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Child { public int AccountId { private get; set; } }
                public class Entity { public Child Customer => new Child(); }
            }
            """]);
        Assert.Equal("PR005", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void ProjectDefaultsApplyAndAttributesOverrideThem()
    {
        var options = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_property.PropertyResolversNamespace"] = "Project.Generated",
            ["build_property.PropertyResolversOutput"] = "Typed"
        });
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            [assembly: GeneratePropertyResolver("TenantId", Namespace = "Override.Generated", ResolverName = "Tenant", Output = ResolverOutput.String, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public int AccountId => 123; public int TenantId => 456; }
                public static class Probe
                {
                    public static string? Run(string scenario) => scenario == "typed"
                        ? Project.Generated.AccountIdResolver.GetAccountId(new Entity())?.ToString()
                        : Override.Generated.Tenant.GetTenantId(new Entity());
                }
            }
            """]);
        var (_, output) = CompilationTestHelper.RunGenerator(compilation, options: options);
        Assert.Equal("123", CompilationTestHelper.InvokeProbe(output, "typed"));
        Assert.Equal("456", CompilationTestHelper.InvokeProbe(output, "string"));
    }

    [Theory]
    [InlineData("PropertyResolversOutput", "Invalid")]
    [InlineData("PropertyResolversRegisterRuntime", "Invalid")]
    public void InvalidProjectDefaultsAreDiagnosed(string property, string value)
    {
        var options = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string> { ["build_property." + property] = value });
        var compilation = CompilationTestHelper.CreateCompilation(["[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\")]"]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true, options: options);
        Assert.Equal("PR010", Assert.Single(result.Diagnostics).Id);
    }

    [Theory]
    [InlineData("TryGet")]
    [InlineData("FormatValue")]
    [InlineData("__GetString")]
    [InlineData("GetAccountId")]
    public void ResolverNamesCannotCollideWithGeneratedMembers(string name)
    {
        var compilation = CompilationTestHelper.CreateCompilation([$"[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver(\"AccountId\", ResolverName = \"{name}\")]"]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR008", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void PropertyNamedStringDoesNotCollideWithFormattingAdapter()
    {
        var (result, _) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("String", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace { public class Entity { public string String => "value"; } }
            """]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void NullableObliviousReferencesAreNullSafeAndFormattingIsInvariant()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            #nullable disable
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Text { public string AccountId => null; }
                public class Number { public decimal AccountId => 1.5m; }
                public static class Probe
                {
                    public static string Run(string scenario)
                    {
                        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                        return TestAssembly.AccountIdResolver.GetAccountId(scenario == "null" ? (object)new Text() : new Number());
                    }
                }
            }
            """]);
        var original = CultureInfo.CurrentCulture;
        try
        {
            Assert.Null(CompilationTestHelper.InvokeProbe(output, "null"));
            Assert.Equal("1.5", CompilationTestHelper.InvokeProbe(output, "value"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ReferencedTypesParticipateInTypedResolution()
    {
        var referenced = CompilationTestHelper.CreateCompilation(["namespace External { public class Entity { public int AccountId => 123; } }"])
            .WithAssemblyName("ExternalAssembly");
        using var stream = new MemoryStream();
        Assert.True(referenced.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.Typed, IncludeNamespaces = new[] { "External" })]
            """], reference);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("global::External.Entity x => x.AccountId", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeRegistryRejectsDuplicateRegistrationsAcrossCaseVariants()
    {
        var name = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        PropertyResolverRegistry.Register(name, _ => "first");
        Assert.Throws<InvalidOperationException>(() => PropertyResolverRegistry.Register(name.ToUpperInvariant(), _ => "second"));
        Assert.True(PropertyResolverRegistry.TryResolve(name, null, out var value));
        Assert.Equal("first", value);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Typed")]
    public void DynamicPropertyValuesDoNotRequireRuntimeBinding(string mode)
    {
        var (_, compilation) = CompilationTestHelper.RunGenerator([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.{{mode}}, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public dynamic AccountId => 123; }
                public static class Probe
                {
                    public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Entity())?.ToString();
                }
            }
            """]);
        Assert.Equal("123", CompilationTestHelper.InvokeProbe(compilation, "value"));
    }

    [Fact]
    public void SiblingInterfacePropertyAmbiguitiesAreDiagnosed()
    {
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("Customer.AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public interface IA { int AccountId { get; } }
                public interface IB { int AccountId { get; } }
                public interface ICustomer : IA, IB { }
                public class Entity { public ICustomer? Customer => null; }
            }
            """]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal("PR009", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void GeneratedTypesCannotBlockAnotherGeneratedNamespace()
    {
        var compilation = CompilationTestHelper.CreateCompilation(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Namespace = "Custom", IncludeNamespaces = new[] { "TestNamespace" })]
            [assembly: GeneratePropertyResolver("TenantId", Namespace = "Custom.AccountIdResolver", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace { public class Entity { public int AccountId => 1; public int TenantId => 2; } }
            """]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("PR008", diagnostic.Id));
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void NestedPathGettersAreEvaluatedOnlyOnce()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("Customer.AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Child { public int Calls; public int AccountId { get { Calls++; return 123; } } }
                public class Entity
                {
                    public int Calls;
                    public Child Child = new Child();
                    public Child Customer { get { Calls++; return Child; } }
                }
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        var entity = new Entity();
                        var found = TestAssembly.CustomerAccountIdResolver.TryGet(entity, out var value);
                        return value + ":" + entity.Calls.ToString() + ":" + entity.Child.Calls.ToString();
                    }
                }
            }
            """]);
        Assert.Equal("123:1:1", CompilationTestHelper.InvokeProbe(output, "value"));
    }

    [Fact]
    public void StaticDispatchInMultipleAssembliesDoesNotShareRegistrationState()
    {
        var dependencies = new Dictionary<string, byte[]>();
        MetadataReference Library(string assembly, string value)
        {
            var input = CompilationTestHelper.CreateCompilation([$$"""
                [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId")]
                namespace {{assembly}}.Models { public class Entity { public string AccountId => "{{value}}"; } }
                """]).WithAssemblyName(assembly);
            var (_, generated) = CompilationTestHelper.RunGenerator(input);
            using var stream = new MemoryStream();
            Assert.True(generated.Emit(stream).Success);
            var bytes = stream.ToArray();
            dependencies.Add(assembly, bytes);
            return MetadataReference.CreateFromImage(bytes);
        }

        var references = new[] { Library("LibraryA", "a"), Library("LibraryB", "b") };
        var compilation = CompilationTestHelper.CreateCompilation(["""
            namespace TestNamespace
            {
                public static class Probe
                {
                    public static string? Run(string scenario)
                    {
                        LibraryA.PropertyResolverDispatch.TryResolve("AccountId", new LibraryA.Models.Entity(), out var a);
                        LibraryB.PropertyResolverDispatch.TryResolve("AccountId", new LibraryB.Models.Entity(), out var b);
                        return a + b;
                    }
                }
            }
            """], references);
        var (_, output) = CompilationTestHelper.RunGenerator(compilation);
        Assert.Equal("ab", CompilationTestHelper.InvokeProbe(output, "value", dependencies));
    }

    [Fact]
    public void ConflictingNamespaceRulesAreDiagnosedEvenWithoutMatchingTypes()
    {
        var (result, _) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("Missing", IncludeNamespaces = new[] { "Domain.Entities" }, ExcludeNamespaces = new[] { "Domain" })]
            """]);
        Assert.Equal("PR006", Assert.Single(result.Diagnostics).Id);
    }

    [Theory]
    [InlineData("Aliases = new string[] { null }", "PR004")]
    [InlineData("Aliases = new[] { \"Invalid..Path\" }", "PR004")]
    [InlineData("Output = (ResolverOutput)999", "PR010")]
    [InlineData("ResolverName = \"Bad-Name\"", "PR004")]
    [InlineData("Namespace = \"Bad-Namespace\"", "PR007")]
    public void InvalidAttributeOptionsAreDiagnosed(string option, string id)
    {
        var compilation = CompilationTestHelper.CreateCompilation([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", {{option}})]
            """]);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: true);
        Assert.Equal(id, Assert.Single(result.Diagnostics).Id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void FileLocalContainingTypesCannotBecomePatternsInAnotherFile()
    {
        var source = """
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                file class Outer { public class Nested { public int AccountId => 123; } }
            }
            """;
        var compilation = CompilationTestHelper.CreateCompilation([]).AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp11)));
        var (result, _) = CompilationTestHelper.RunGenerator(compilation);
        Assert.Equal("PR005", Assert.Single(result.Diagnostics).Id);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("Nested x", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedCaseVariantDoesNotMakeAnEligiblePropertyAmbiguous()
    {
        var (result, output) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public int AccountId => 123; public static int ACCOUNTID => 456; }
                public static class Probe { public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Entity()); }
            }
            """]);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("123", CompilationTestHelper.InvokeProbe(output, "value"));
    }

    [Fact]
    public void ArraysExposeTheirOwnPropertiesWithoutElementTraversal()
    {
        var (_, output) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("Items.Length", Output = PropertyResolvers.Attributes.ResolverOutput.Typed, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace
            {
                public class Entity { public int[]? Items => new[] { 1, 2, 3 }; }
                public static class Probe { public static string? Run(string scenario) => TestAssembly.ItemsLengthResolver.GetItemsLength(new Entity())?.ToString(); }
            }
            """]);
        Assert.Equal("3", CompilationTestHelper.InvokeProbe(output, "value"));
    }

    [Fact]
    public void NamespaceFilteringUsesUnescapedKeywordNames()
    {
        var (result, _) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "class" })]
            namespace @class { public class Entity { public int AccountId => 123; } }
            """]);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("global::@class.Entity x =>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasOnlyPropertyTypesAreFormattedOrDiagnosedWithoutInvalidSignatures(bool typed)
    {
        var external = CompilationTestHelper.CreateCompilation(["namespace External { public class Value { } }"])
            .WithAssemblyName("ExternalAssembly");
        using var stream = new MemoryStream();
        Assert.True(external.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray(),
            new MetadataReferenceProperties(aliases: ImmutableArray.Create("ExternalAlias")));
        var compilation = CompilationTestHelper.CreateCompilation([$$"""
            extern alias ExternalAlias;
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.{{(typed ? "Typed" : "String")}}, IncludeNamespaces = new[] { "TestNamespace" })]
            namespace TestNamespace { public class Entity { public ExternalAlias::External.Value AccountId => new ExternalAlias::External.Value(); } }
            """], reference);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: typed);
        if (typed)
        {
            Assert.Equal("PR003", Assert.Single(result.Diagnostics).Id);
        }
        else
        {
            Assert.Empty(result.Diagnostics);
        }
    }

    [Fact]
    public void SourceTypesShadowingReferencedRootsCannotProduceInvalidPatterns()
    {
        var external = CompilationTestHelper.CreateCompilation(["namespace External { public class Entity { public int AccountId => 123; } }"])
            .WithAssemblyName("ExternalAssembly").ToMetadataReference();
        var (result, _) = CompilationTestHelper.RunGenerator(["""
            [assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "External" })]
            namespace External { public class Entity { public string Other => "local"; } }
            """], external);
        Assert.Equal("PR002", Assert.Single(result.Diagnostics).Id);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("Entity x =>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceTypesShadowingReferencedResultTypesAreFormattedOrDiagnosed(bool typed)
    {
        var external = CompilationTestHelper.CreateCompilation(["""
            namespace External
            {
                public class Value { }
                public class Entity { public Value AccountId => new Value(); }
            }
            """]).WithAssemblyName("ExternalAssembly").ToMetadataReference();
        var compilation = CompilationTestHelper.CreateCompilation([$$"""
            using PropertyResolvers.Attributes;
            [assembly: GeneratePropertyResolver("AccountId", Output = ResolverOutput.{{(typed ? "Typed" : "String")}}, IncludeNamespaces = new[] { "External" })]
            namespace External { public class Value { } }
            """], external);
        var (result, _) = CompilationTestHelper.RunGenerator(compilation, allowGeneratorErrors: typed);
        if (typed)
        {
            Assert.Equal("PR003", Assert.Single(result.Diagnostics).Id);
        }
        else
        {
            Assert.Empty(result.Diagnostics);
        }
    }
}
