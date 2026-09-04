using System.Diagnostics;

namespace RazorScopedStyleElements.IntegrationTests;

internal sealed class DotNetProject : IAsyncDisposable
{
    private DotNetProject(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string ProjectFile => System.IO.Directory.EnumerateFiles(Directory, "*.csproj").Single();

    public static DotNetProject CreateEmpty()
    {
        var directory = CreateTestDirectory();
        System.IO.Directory.CreateDirectory(directory);
        return new DotNetProject(directory);
    }

    public static async Task<DotNetProject> CreateAsync(string template, string name)
    {
        var directory = CreateTestDirectory();
        System.IO.Directory.CreateDirectory(directory);

        var project = new DotNetProject(directory);
        await project.RunAsync("new", template, "--name", name, "--output", ".", "--framework", "net10.0", "--no-restore");
        return project;
    }

    private static string CreateTestDirectory()
    {
        var tempPath = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && tempPath.StartsWith("/var/", StringComparison.Ordinal))
        {
            tempPath = "/private" + tempPath;
        }

        return Path.Combine(tempPath, "RazorScopedStyleElements.Tests", Guid.NewGuid().ToString("N"));
    }

    public async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        var result = await RunCoreAsync(arguments);
        Assert.True(result.Succeeded, $"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{result.Output}");
        return result;
    }

    public async Task<ProcessResult> RunExpectingFailureAsync(params string[] arguments)
    {
        var result = await RunCoreAsync(arguments);
        Assert.False(result.Succeeded, $"dotnet {string.Join(' ', arguments)} unexpectedly succeeded.");
        return result;
    }

    private async Task<ProcessResult> RunCoreAsync(string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.StartInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";

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

        return new ProcessResult(process.ExitCode, await standardOutput + await standardError);
    }

    public void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public ValueTask DisposeAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RSSE_KEEP_TEMP"), "1", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the build assertion that owns this fixture.
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}
