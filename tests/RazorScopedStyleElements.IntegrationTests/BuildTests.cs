namespace RazorScopedStyleElements.IntegrationTests;

public sealed class BuildTests
{
    [Fact]
    public async Task TrivialBlazorProjectBuilds()
    {
        await using var project = await DotNetProject.CreateAsync("blazor", "TrivialBlazorApp");

        await project.RunAsync("build", "--nologo");

        Assert.True(File.Exists(Path.Combine(project.Directory, "bin", "Debug", "net10.0", "TrivialBlazorApp.dll")));
    }
}
