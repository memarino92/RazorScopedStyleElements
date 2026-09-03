using System.Diagnostics;
using System.Text;

namespace RazorInlineCss.IntegrationTests;

internal sealed class DotNetProject : IAsyncDisposable
{
    private DotNetProject(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string ProjectFile => System.IO.Directory.EnumerateFiles(Directory, "*.csproj").Single();

    public static async Task<DotNetProject> CreateAsync(string template, string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RazorInlineCss.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);

        var project = new DotNetProject(directory);
        await project.RunAsync("new", template, "--name", name, "--output", ".", "--framework", "net10.0", "--no-restore");
        return project;
    }

    public async Task<ProcessResult> RunAsync(params string[] arguments)
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
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) => AppendLine(output, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendLine(output, eventArgs.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var result = new ProcessResult(process.ExitCode, output.ToString());
        Assert.True(result.Succeeded, $"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{result.Output}");
        return result;
    }

    public void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public ValueTask DisposeAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RICSS_KEEP_TEMP"), "1", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the build assertion that owns this fixture.
        }

        return ValueTask.CompletedTask;
    }

    private static void AppendLine(StringBuilder output, string? value)
    {
        if (value is not null)
        {
            output.AppendLine(value);
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}
