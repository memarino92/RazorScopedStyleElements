using System.Xml.Linq;

namespace RazorScopedStyleElements.IntegrationTests;

public sealed class SdkScenarioTests
{
    [Fact]
    public async Task InlineAndConventionalScopedCssBuildSideBySideInRelease()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("MixedCssApp");
        project.WriteFile("Components/Inline.razor", "<p class=\"inline-rule\">Inline</p><style>.inline-rule { color: purple; }</style>");
        project.WriteFile("Components/Conventional.razor", "<p class=\"conventional-rule\">Conventional</p>");
        project.WriteFile("Components/Conventional.razor.css", ".conventional-rule { color: teal; }");

        await project.RunAsync("build", "--configuration", "Release", "--nologo");

        var bundle = await File.ReadAllTextAsync(Path.Combine(project.Directory, "obj", "Release", "net10.0", "scopedcss", "bundle", "MixedCssApp.styles.css"));
        Assert.Contains(".inline-rule[b-", bundle, StringComparison.Ordinal);
        Assert.Contains(".conventional-rule[b-", bundle, StringComparison.Ordinal);
        await project.RunAsync("publish", "--configuration", "Release", "--no-restore", "--nologo");
    }

    [Fact]
    public async Task InlineAndSiblingScopedCssReportsCollision()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("CollisionApp");
        project.WriteFile("Components/Collision.razor", "<p>Collision</p><style>p { color: red; }</style>");
        project.WriteFile("Components/Collision.razor.css", "p { color: blue; }");

        var result = await project.RunExpectingFailureAsync("build", "--nologo");

        Assert.Contains("RICSS004", result.Output, StringComparison.Ordinal);
        Assert.Contains("Collision.razor", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiTargetBuildUsesSeparateIntermediateDirectories()
    {
        await using var project = await PackageConsumer.CreateAsync("razorclasslib", "MultiTargetLibrary");
        var projectDocument = XDocument.Load(project.ProjectFile, LoadOptions.PreserveWhitespace);
        var targetFramework = projectDocument.Descendants().Single(element => element.Name.LocalName == "TargetFramework");
        targetFramework.Name = targetFramework.Name.Namespace + "TargetFrameworks";
        targetFramework.Value = "net9.0;net10.0";

        var net10Reference = projectDocument.Descendants().Single(element =>
            element.Name.LocalName == "PackageReference" &&
            string.Equals((string?)element.Attribute("Include"), "Microsoft.AspNetCore.Components.Web", StringComparison.Ordinal));
        net10Reference.SetAttributeValue("Condition", "'$(TargetFramework)' == 'net10.0'");
        var net9Reference = new XElement(net10Reference);
        net9Reference.SetAttributeValue("Version", "9.0.0");
        net9Reference.SetAttributeValue("Condition", "'$(TargetFramework)' == 'net9.0'");
        net10Reference.AddBeforeSelf(net9Reference);
        projectDocument.Save(project.ProjectFile);
        project.WriteFile("Components/Multi.razor", "<p class=\"multi\">Multi</p><style>.multi { color: navy; }</style>");

        await project.RunAsync("build", "--configuration", "Release", "--nologo");

        Assert.True(File.Exists(Path.Combine(project.Directory, "obj", "Release", "net9.0", "RazorScopedStyleElements", "razor", "Components", "Multi.razor")));
        Assert.True(File.Exists(Path.Combine(project.Directory, "obj", "Release", "net10.0", "RazorScopedStyleElements", "razor", "Components", "Multi.razor")));
    }

    [Fact]
    public async Task RazorClassLibraryCssFlowsIntoConsumingApp()
    {
        var package = await PackageFixture.GetPackageAsync();
        await using var root = DotNetProject.CreateEmpty();
        root.WriteFile("NuGet.Config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config><add key="globalPackagesFolder" value=".packages" /></config>
              <packageSources>
                <clear />
                <add key="local" value="{{Path.GetDirectoryName(package)}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        await root.RunAsync("new", "blazor", "--name", "RclHost", "--output", "App", "--framework", "net10.0", "--no-restore");
        await root.RunAsync("new", "razorclasslib", "--name", "InlineLibrary", "--output", "Library", "--framework", "net10.0", "--no-restore");

        var libraryProject = Path.Combine(root.Directory, "Library", "InlineLibrary.csproj");
        var libraryText = await File.ReadAllTextAsync(libraryProject);
        libraryText = libraryText.Replace("</Project>", "<ItemGroup><PackageReference Include=\"RazorScopedStyleElements\" Version=\"0.1.0\" /></ItemGroup></Project>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(libraryProject, libraryText);
        root.WriteFile("Library/InlineCard.razor", "<article class=\"rcl-inline\">RCL</article><style>.rcl-inline { color: maroon; }</style>");

        var appProject = Path.Combine(root.Directory, "App", "RclHost.csproj");
        var appText = await File.ReadAllTextAsync(appProject);
        appText = appText.Replace("</Project>", "<ItemGroup><ProjectReference Include=\"../Library/InlineLibrary.csproj\" /></ItemGroup></Project>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(appProject, appText);

        await root.RunAsync("build", appProject, "--configuration", "Release", "--nologo");
        var bundle = await File.ReadAllTextAsync(Path.Combine(root.Directory, "App", "obj", "Release", "net10.0", "scopedcss", "bundle", "RclHost.styles.css"));
        Assert.Contains("_content/InlineLibrary/InlineLibrary", bundle, StringComparison.Ordinal);
        var libraryBundle = await File.ReadAllTextAsync(Path.Combine(root.Directory, "Library", "obj", "Release", "net10.0", "scopedcss", "projectbundle", "InlineLibrary.bundle.scp.css"));
        Assert.Contains(".rcl-inline[b-", libraryBundle, StringComparison.Ordinal);
        await root.RunAsync("publish", appProject, "--configuration", "Release", "--no-restore", "--nologo");
    }
}
