using System;
using System.Linq;
using PropertyResolvers.Tests.Utils;
using Xunit;

namespace PropertyResolvers.Tests;

public class PropertyResolverReliabilityTests
{
    [Theory]
    [InlineData("public class Entity { public static string AccountId => \"static\"; }")]
    [InlineData("public class Entity { public string AccountId { private get; set; } = \"private\"; }")]
    [InlineData("public ref struct Entity { public string AccountId => \"ref\"; }")]
    [InlineData("public static class Entity { public static string AccountId => \"static\"; }")]
    [InlineData("public class Outer { private class Entity { public string AccountId => \"private\"; } }")]
    [InlineData("internal class Outer { public class Entity { private class Hidden { public string AccountId => \"hidden\"; } } }")]
    [InlineData("public class Outer<T> { public class Entity { public string AccountId => \"generic\"; } }")]
    [InlineData("public class Entity { public System.Span<int> AccountId => default; }")]
    [InlineData("public unsafe class Entity { public int* AccountId => null; }")]
    [InlineData("public unsafe class Entity { public delegate*<void> AccountId => null; }")]
    public void UnsupportedTypesAndPropertiesDoNotProduceInvalidArms(string declaration)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace { {{declaration}} }
                       """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        var code = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.DoesNotContain(" x =>", code);
    }

    [Fact]
    public void IndexersDoNotProduceInvalidArms()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("Item", IncludeNamespaces = new[] { "TestNamespace" })]
                              namespace TestNamespace
                              {
                                  public class Entity { public string this[int index] => "indexed"; }
                              }
                              """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        var code = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ItemResolver.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.DoesNotContain(" x =>", code);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("record")]
    [InlineData("record struct")]
    public void SupportedTypeShapesResolveAtRuntime(string typeKind)
    {
        var source = $$"""
                       #nullable enable
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace
                       {
                           public {{typeKind}} Entity { public string? AccountId { get; set; } }
                           public static class Probe
                           {
                               public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(
                                   scenario switch
                                   {
                                       "value" => new Entity { AccountId = "ACC-123" },
                                       "null-value" => new Entity(),
                                       "unknown" => new object(),
                                       _ => null
                                   });
                           }
                       }
                       """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("ACC-123", CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Null(CompilationTestHelper.InvokeProbe(output, "null-value"));
        Assert.Null(CompilationTestHelper.InvokeProbe(output, "unknown"));
        Assert.Null(CompilationTestHelper.InvokeProbe(output, "null-source"));
    }

    [Theory]
    [InlineData("class", "public new string AccountId => \"derived\";", "derived")]
    [InlineData("class", "public override string AccountId => \"derived\";", "derived")]
    [InlineData("class", "", "base")]
    [InlineData("record", "public override string AccountId => \"derived\";", "derived")]
    [InlineData("record", "", "base")]
    public void InheritanceResolvesMostSpecificProperty(string typeKind, string derivedProperty, string expected)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace
                       {
                           public {{typeKind}} Base { public virtual string AccountId => "base"; }
                           public {{typeKind}} Derived : Base { {{derivedProperty}} }
                           public static class Probe
                           {
                               public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Derived());
                           }
                       }
                       """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal(expected, CompilationTestHelper.InvokeProbe(output, "derived"));
    }

    [Fact]
    public void PropertiesInheritedFromConstructedGenericBasesResolve()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                              namespace TestNamespace
                              {
                                  public class Base<T> { public string AccountId => "generic-base"; }
                                  public class Derived : Base<int> { }
                                  public static class Probe
                                  {
                                      public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Derived());
                                  }
                              }
                              """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("generic-base", CompilationTestHelper.InvokeProbe(output, "derived"));
    }

    [Fact]
    public void KeywordPropertyAndTypeNamesAreEscaped()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("event", IncludeNamespaces = new[] { "TestNamespace" })]
                              namespace TestNamespace
                              {
                                  public class @class { public string @event => "escaped"; }
                                  public static class Probe
                                  {
                                      public static string? Run(string scenario) => TestAssembly.eventResolver.Getevent(new @class());
                                  }
                              }
                              """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("escaped", CompilationTestHelper.InvokeProbe(output, "keyword"));
    }

    [Fact]
    public void NullableValuesResolveAtRuntime()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                              namespace TestNamespace
                              {
                                  public class Entity { public int? AccountId { get; set; } }
                                  public static class Probe
                                  {
                                      public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(
                                          new Entity { AccountId = scenario == "value" ? 123 : null });
                                  }
                              }
                              """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("123", CompilationTestHelper.InvokeProbe(output, "value"));
        Assert.Null(CompilationTestHelper.InvokeProbe(output, "null-value"));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    public void AccessibleNestedTypesResolve(string outerAccessibility)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace TestNamespace
                       {
                           {{outerAccessibility}} class Outer
                           {
                               public class Entity { public string AccountId => "nested"; }
                           }
                           public static class Probe
                           {
                               public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Outer.Entity());
                           }
                       }
                       """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("nested", CompilationTestHelper.InvokeProbe(output, "nested"));
    }

    [Theory]
    [InlineData("public new int AccountId;")]
    [InlineData("public new static string AccountId => \"hidden\";")]
    [InlineData("private new string AccountId => \"hidden\";")]
    public void HiddenBasePropertiesAreNotSelectedThroughDerivedTypes(string hidingMember)
    {
        var source = $$"""
                       using PropertyResolvers.Attributes;
                       [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                       namespace OtherNamespace
                       {
                           public class Base { public string AccountId => "base"; }
                       }
                       namespace TestNamespace
                       {
                           public class Derived : OtherNamespace.Base { {{hidingMember}} }
                       }
                       """;

        var (result, _) = CompilationTestHelper.RunGenerator([source]);
        var code = result.GeneratedTrees.Single(tree => tree.FilePath.EndsWith("AccountIdResolver.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.DoesNotContain(" x =>", code);
    }

    [Fact]
    public void InheritedPropertiesResolveWhenBaseNamespaceIsExcluded()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" })]
                              namespace OtherNamespace
                              {
                                  public class Base { public string AccountId => "inherited"; }
                              }
                              namespace TestNamespace
                              {
                                  public class Derived : OtherNamespace.Base { }
                                  public static class Probe
                                  {
                                      public static string? Run(string scenario) => TestAssembly.AccountIdResolver.GetAccountId(new Derived());
                                  }
                              }
                              """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("inherited", CompilationTestHelper.InvokeProbe(output, "derived"));
    }

    [Fact]
    public void ModuleInitializerRegistersWorkingResolvers()
    {
        const string source = """
                              using PropertyResolvers.Attributes;
                              [assembly: GeneratePropertyResolver("AccountId", IncludeNamespaces = new[] { "TestNamespace" }, RegisterRuntime = true)]
                              namespace TestNamespace
                              {
                                  public class Entity { public string AccountId => "registered"; }
                                  public static class Probe
                                  {
                                      public static string? Run(string scenario) =>
                                          PropertyResolverRegistry.TryResolve("accountid", new Entity(), out var value) ? value : "missing";
                                  }
                              }
                              """;

        var (_, output) = CompilationTestHelper.RunGenerator([source]);
        Assert.Equal("registered", CompilationTestHelper.InvokeProbe(output, "registry"));
    }
}
