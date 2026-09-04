namespace RazorScopedStyleElements.IntegrationTests;

public sealed class IncrementalBuildTests
{
    [Fact]
    public async Task GeneratedFilesAreStableAndStaleCssIsNotRegistered()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("IncrementalApp");
        project.WriteFile("Components/Foo.razor", Component("foo", "red"));
        project.WriteFile("Components/Bar.razor", Component("bar", "blue"));

        await project.RunAsync("build", "--nologo");

        var generatedRoot = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "RazorScopedStyleElements");
        var fooRazor = Path.Combine(generatedRoot, "razor", "Components", "Foo.razor");
        var fooCss = Path.Combine(generatedRoot, "css", "Components", "Foo.razor.css");
        var barRazor = Path.Combine(generatedRoot, "razor", "Components", "Bar.razor");
        var barCss = Path.Combine(generatedRoot, "css", "Components", "Bar.razor.css");
        var initialTimes = GetWriteTimes(fooRazor, fooCss, barRazor, barCss);

        await Task.Delay(1200);
        await project.RunAsync("build", "--no-restore", "--nologo");
        Assert.Equal(initialTimes, GetWriteTimes(fooRazor, fooCss, barRazor, barCss));

        await Task.Delay(1200);
        project.WriteFile("Components/Foo.razor", Component("foo", "green").Replace(">foo</p>", ">foo updated</p>", StringComparison.Ordinal));
        await project.RunAsync("build", "--no-restore", "--nologo");
        var editedTimes = GetWriteTimes(fooRazor, fooCss, barRazor, barCss);
        Assert.True(editedTimes[0] > initialTimes[0]);
        Assert.True(editedTimes[1] > initialTimes[1]);
        Assert.Equal(initialTimes[2], editedTimes[2]);
        Assert.Equal(initialTimes[3], editedTimes[3]);
        Assert.Contains(">foo updated</p>", await File.ReadAllTextAsync(fooRazor), StringComparison.Ordinal);
        Assert.Contains("color: green", await File.ReadAllTextAsync(fooCss), StringComparison.Ordinal);

        project.WriteFile("Components/Foo.razor", "<p class=\"foo\">No inline style</p>");
        await project.RunAsync("build", "--no-restore", "--nologo");
        Assert.DoesNotContain(".foo[b-", await ReadBundleAsync(project), StringComparison.Ordinal);

        File.Delete(Path.Combine(project.Directory, "Components", "Bar.razor"));
        await project.RunAsync("build", "--no-restore", "--nologo");
        Assert.DoesNotContain(".bar[b-", await ReadBundleAsync(project), StringComparison.Ordinal);

        await project.RunAsync("clean", "--nologo");
        Assert.False(Directory.Exists(generatedRoot));
    }

    [Fact]
    public async Task KillSwitchBypassesGeneration()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("DisabledApp");
        project.WriteFile("Components/Disabled.razor", Component("disabled", "red"));

        await project.RunAsync("build", "--nologo", "-p:RazorScopedStyleElementsEnabled=false");

        var generatedRoot = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "RazorScopedStyleElements");
        Assert.False(Directory.Exists(generatedRoot));
        Assert.DoesNotContain(".disabled[b-", await ReadBundleAsync(project), StringComparison.Ordinal);
    }

    private static string Component(string className, string color) => $$"""
        <p class="{{className}}">{{className}}</p>
        <style>
            .{{className}} { color: {{color}}; }
        </style>
        """;

    private static DateTime[] GetWriteTimes(params string[] paths) =>
        paths.Select(File.GetLastWriteTimeUtc).ToArray();

    private static async Task<string> ReadBundleAsync(DotNetProject project)
    {
        var path = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "scopedcss", "bundle", Path.GetFileNameWithoutExtension(project.ProjectFile) + ".styles.css");
        return await File.ReadAllTextAsync(path);
    }
}
