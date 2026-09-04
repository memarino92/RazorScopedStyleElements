namespace RazorScopedStyleElements.Tasks.Tests;

public sealed class InlineStyleExtractorTests
{
    [Fact]
    public void NoStyleIsUntouched()
    {
        const string source = "<h1>Hello</h1>";

        var result = InlineStyleExtractor.Extract(source);

        Assert.False(result.HasStyle);
        Assert.Equal(source, result.TransformedSource);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("<style>h1 { color: red; }</style>\n<h1>Hello</h1>")]
    [InlineData("<h1>Hello</h1>\n<style>h1 { color: red; }</style>")]
    [InlineData("<h1>Hello</h1>\n<style>h1 { color: red; }</style>\n@code { }")]
    public void ExtractsOneTopLevelStyleRegardlessOfPosition(string source)
    {
        var result = InlineStyleExtractor.Extract(source);

        Assert.True(result.HasStyle);
        Assert.Equal("h1 { color: red; }" + Environment.NewLine, result.Css);
        Assert.DoesNotContain("<style>", result.TransformedSource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source.Count(character => character == '\n'), result.TransformedSource.Count(character => character == '\n'));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PreservesComplexStaticCss()
    {
        const string source = """
            <style>
            /* braces { } and <style> text */
            .card::before { content: "<style> { }"; }
            ::deep a:hover { color: rebeccapurple; }
            @media (width > 40rem) { .card { display: grid; } }
            @supports (display: subgrid) { .card { grid-template: subgrid; } }
            </style>
            <article class="card"></article>
            """;

        var result = InlineStyleExtractor.Extract(source);

        Assert.True(result.HasStyle);
        Assert.Contains("@media", result.Css);
        Assert.Contains("::deep", result.Css);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void IgnoresStyleTextInsideRazorCodeAndComments()
    {
        const string source = """
            @* <style>not an element</style> *@
            <p>@("<style>also not an element</style>")</p>
            @code {
                private const string Example = "<style>still not an element</style>";
            }
            """;

        var result = InlineStyleExtractor.Extract(source);

        Assert.False(result.HasStyle);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReportsMultipleStyleElements()
    {
        var result = InlineStyleExtractor.Extract("<style>.a { }</style>\n<style>.b { }</style>");

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "RSSE001");
        Assert.Equal(2, diagnostic.Line);
        Assert.False(result.HasStyle);
    }

    [Theory]
    [InlineData("<div><style>.a { }</style></div>")]
    [InlineData("@if (true) { <style>.a { }</style> }")]
    public void ReportsNestedOrConditionalStyle(string source)
    {
        var result = InlineStyleExtractor.Extract(source);

        Assert.Contains(result.Diagnostics, item => item.Id == "RSSE002");
        Assert.False(result.HasStyle);
    }

    [Theory]
    [InlineData("<style>")]
    [InlineData("<style scoped>.a { }</style>")]
    [InlineData("<style>.a { color: @color; }</style>")]
    [InlineData("<style>.a { }</style>\n@code {")]
    public void ReportsMalformedOrDynamicSyntax(string source)
    {
        var result = InlineStyleExtractor.Extract(source);

        Assert.Contains(result.Diagnostics, item => item.Id == "RSSE003");
        Assert.False(result.HasStyle);
    }

    [Fact]
    public void SupportsEmptyStyle()
    {
        var result = InlineStyleExtractor.Extract("<style>\n</style>\n<p>Hello</p>");

        Assert.True(result.HasStyle);
        Assert.Equal(Environment.NewLine, result.Css);
        Assert.Empty(result.Diagnostics);
    }
}
