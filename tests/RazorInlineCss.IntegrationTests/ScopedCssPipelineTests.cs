namespace RazorInlineCss.IntegrationTests;

public sealed class ScopedCssPipelineTests
{
    [Fact]
    public async Task PackedPackageFeedsGeneratedCssToMicrosoftPipelineOnBuildAndPublish()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("ScopedCssProofApp");

        project.WriteFile("Components/Pages/InlineStyle.razor", """
            @page "/inline-style"

            <h1 class="ricss-proof">Count: @count</h1>
            <button @onclick="Increment">Increment</button>

            <style>
                .ricss-proof {
                    color: rebeccapurple;
                }
            </style>

            @code {
                private int count;

                private void Increment() => count++;
            }
            """);
        var originalSource = await File.ReadAllTextAsync(Path.Combine(project.Directory, "Components", "Pages", "InlineStyle.razor"));

        await project.RunAsync("build", "--nologo");

        var bundle = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "scopedcss", "bundle", "ScopedCssProofApp.styles.css");
        var css = await File.ReadAllTextAsync(bundle);
        Assert.Matches(@"\.ricss-proof\[b-[a-z0-9]+\]", css);

        var transformed = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "RazorInlineCss", "razor", "Components", "Pages", "InlineStyle.razor");
        Assert.True(File.Exists(transformed));
        Assert.DoesNotContain("<style>", await File.ReadAllTextAsync(transformed), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalSource, await File.ReadAllTextAsync(Path.Combine(project.Directory, "Components", "Pages", "InlineStyle.razor")));

        await project.RunAsync("publish", "--no-restore", "--nologo");
        Assert.Contains(
            System.IO.Directory.EnumerateFiles(Path.Combine(project.Directory, "bin", "Release", "net10.0", "publish", "wwwroot"), "*.styles.css", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains(".ricss-proof[b-", StringComparison.Ordinal));
    }
}
