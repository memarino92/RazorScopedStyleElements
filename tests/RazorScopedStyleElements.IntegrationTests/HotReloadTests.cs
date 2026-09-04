using System.Diagnostics;
using System.Text;

namespace RazorScopedStyleElements.IntegrationTests;

public sealed class HotReloadTests
{
    [Fact]
    public async Task InlineMarkupAndStylesAreAppliedByDotNetWatchRestart()
    {
        await using var project = await PackageConsumer.CreateBlazorAsync("WatchApp");
        project.WriteFile("Components/Watched.razor", "<p>Before</p><style>p { color: red; }</style>");
        await project.RunAsync("build", "--nologo");

        var output = new StringBuilder();
        using var process = StartWatch(project, output);

        try
        {
            await WaitForAsync(() => CountOccurrences(ReadOutput(output), "Application started") == 1);
            await Task.Delay(3000);

            project.WriteFile("Components/Watched.razor", "<p>After</p><style>p { color: blue; }</style>");

            await WaitForAsync(() => CountOccurrences(ReadOutput(output), "Application started") >= 2);

            var generatedRoot = Path.Combine(project.Directory, "obj", "Debug", "net10.0", "RazorScopedStyleElements");
            Assert.Contains("<p>After</p>", await File.ReadAllTextAsync(Path.Combine(generatedRoot, "razor", "Components", "Watched.razor")), StringComparison.Ordinal);
            Assert.Contains("color: blue", await File.ReadAllTextAsync(Path.Combine(generatedRoot, "css", "Components", "Watched.razor.css")), StringComparison.Ordinal);
        }
        catch (TimeoutException exception)
        {
            throw new Xunit.Sdk.XunitException($"{exception.Message}{Environment.NewLine}{ReadOutput(output)}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static Process StartWatch(DotNetProject project, StringBuilder output)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = project.Directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
            EnableRaisingEvents = true,
        };
        process.StartInfo.ArgumentList.Add("watch");
        process.StartInfo.ArgumentList.Add("--no-hot-reload");
        process.StartInfo.ArgumentList.Add("--no-restore");
        process.StartInfo.ArgumentList.Add("--no-launch-profile");
        process.StartInfo.ArgumentList.Add("--urls");
        process.StartInfo.ArgumentList.Add("http://127.0.0.1:0");
        process.StartInfo.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        process.OutputDataReceived += (_, eventArgs) => AppendOutput(output, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendOutput(output, eventArgs.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void AppendOutput(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }

    private static string ReadOutput(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("Timed out waiting for dotnet watch to restart the application.");
            }

            await Task.Delay(100);
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0; index += search.Length)
        {
            count++;
        }

        return count;
    }
}
