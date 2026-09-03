using System.IO.Compression;

namespace RazorInlineCss.IntegrationTests;

public sealed class PackageContentsTests
{
    [Fact]
    public async Task PackageContainsBuildAssetsTaskDocumentationAndLicense()
    {
        var package = await PackageFixture.GetPackageAsync();

        using var archive = ZipFile.OpenRead(package);
        var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("build/RazorInlineCss.props", entries);
        Assert.Contains("build/RazorInlineCss.targets", entries);
        Assert.Contains("tasks/net10.0/RazorInlineCss.Tasks.dll", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains("LICENSE.txt", entries);
        Assert.DoesNotContain(entries, entry => entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));
    }
}
