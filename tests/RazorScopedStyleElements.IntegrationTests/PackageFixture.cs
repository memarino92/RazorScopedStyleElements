namespace RazorScopedStyleElements.IntegrationTests;

internal static class PackageFixture
{
    private static readonly SemaphoreSlim PackLock = new(1, 1);
    private static string? packagePath;

    public static async Task<string> GetPackageAsync()
    {
        await PackLock.WaitAsync();
        try
        {
            if (packagePath is not null && File.Exists(packagePath))
            {
                return packagePath;
            }

            var feed = Path.Combine(Path.GetTempPath(), "RazorScopedStyleElements.Tests", "packages", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(feed);
            var project = Path.Combine(RepositoryPaths.Root, "src", "RazorScopedStyleElements.Package", "RazorScopedStyleElements.Package.csproj");

            await RunDotNetAsync(RepositoryPaths.Root, "pack", project, "--configuration", "Debug", "--output", feed, "--nologo");
            packagePath = Path.Combine(feed, "RazorScopedStyleElements.0.1.0.nupkg");
            Assert.True(File.Exists(packagePath), $"Package was not created at {packagePath}.");
            return packagePath;
        }
        finally
        {
            PackLock.Release();
        }
    }

    private static async Task RunDotNetAsync(string workingDirectory, params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out:{Environment.NewLine}{await standardOutput}{await standardError}");
        }

        var output = await standardOutput + await standardError;
        Assert.True(process.ExitCode == 0, output);
    }
}
