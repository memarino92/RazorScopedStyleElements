namespace RazorInlineCss.IntegrationTests;

public sealed class ScopedCssPipelineTests
{
    [Fact]
    public async Task PackedPackageFeedsGeneratedCssToMicrosoftPipelineOnBuildAndPublish()
    {
        var package = await PackageFixture.GetPackageAsync();
        await using var project = await DotNetProject.CreateAsync("blazor", "ScopedCssProofApp");

        project.WriteFile("NuGet.Config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value=".packages" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{Path.GetDirectoryName(package)}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        var projectText = await File.ReadAllTextAsync(project.ProjectFile);
        projectText = projectText.Replace(
            "</Project>",
            """
              <ItemGroup>
                <PackageReference Include="RazorInlineCss" Version="0.1.0" />
                <Content Update="Components/Pages/Home.razor" RazorInlineCssProof="true" />
              </ItemGroup>
            </Project>
            """,
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(project.ProjectFile, projectText);

        await project.RunAsync("build", "--nologo");

        var bundle = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "scopedcss", "bundle", "ScopedCssProofApp.styles.css");
        var css = await File.ReadAllTextAsync(bundle);
        Assert.Matches(@"\.ricss-proof\[b-[a-z0-9]+\]", css);

        await project.RunAsync("publish", "--no-restore", "--nologo");
        Assert.Contains(
            System.IO.Directory.EnumerateFiles(Path.Combine(project.Directory, "bin", "Release", "net10.0", "publish", "wwwroot"), "*.styles.css", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains(".ricss-proof[b-", StringComparison.Ordinal));
    }
}
