using CodeRag.Core.Parsing;
using Xunit;

namespace CodeRag.Core.Tests.Parsing;

public class CSharpSyntaxExtractorTests
{
    private readonly CSharpSyntaxExtractor _sut = new();

    [Fact]
    public void Extract_SimpleClass_ReturnsClassChunk()
    {
        var source = """
            namespace MyApp.Services;
            public class OrderService
            {
                public void ProcessOrder(int id) { }
            }
            """;

        var chunks = _sut.Extract(source, "src/Services/OrderService.cs");

        Assert.Contains(chunks, c => c.Kind == SymbolKind.Class && c.SymbolName == "OrderService");
        Assert.Contains(chunks, c => c.Kind == SymbolKind.Method && c.SymbolName == "ProcessOrder");
    }

    [Fact]
    public void Extract_Method_HasCorrectRelativePath()
    {
        var source = "public class Foo { public void Bar() {} }";
        var chunks = _sut.Extract(source, "src/Foo.cs");

        Assert.All(chunks, c => Assert.Equal("src/Foo.cs", c.RelativePath));
    }

    [Fact]
    public void Extract_Method_HasParentClass()
    {
        var source = "public class MyClass { public void MyMethod() {} }";
        var chunks = _sut.Extract(source, "test.cs");

        var method = Assert.Single(chunks, c => c.Kind == SymbolKind.Method);
        Assert.Equal("MyClass", method.ParentClass);
    }

    [Fact]
    public void Extract_SameContent_SameContentHash()
    {
        var source = "public class Foo { public void Bar() {} }";
        var chunks1 = _sut.Extract(source, "src/Foo.cs");
        var chunks2 = _sut.Extract(source, "src/Foo.cs");

        Assert.Equal(
            chunks1.Select(c => c.ContentHash),
            chunks2.Select(c => c.ContentHash));
    }

    [Fact]
    public void Extract_Property_Detected()
    {
        var source = "public class Foo { public int Id { get; set; } }";
        var chunks = _sut.Extract(source, "Foo.cs");

        Assert.Contains(chunks, c => c.Kind == SymbolKind.Property && c.SymbolName == "Id");
    }
}
