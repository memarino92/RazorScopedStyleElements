namespace RazorInlineCss.IntegrationTests;

internal static class PackageConsumer
{
    public static async Task<DotNetProject> CreateBlazorAsync(string name)
    {
        var package = await PackageFixture.GetPackageAsync();
        var project = await DotNetProject.CreateAsync("blazor", name);

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
              </ItemGroup>
            </Project>
            """,
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(project.ProjectFile, projectText);
        return project;
    }
}
