using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace RazorInlineCss.Tasks;

public sealed class GenerateProofScopedCss : Microsoft.Build.Utilities.Task
{
    private const string ProofCss = ".ricss-proof { color: rebeccapurple; }";

    [Required]
    public required ITaskItem[] RazorComponents { get; init; }

    [Required]
    public required string OutputDirectory { get; init; }

    [Output]
    public ITaskItem[] GeneratedCss { get; private set; } = [];

    public override bool Execute()
    {
        var generated = new List<ITaskItem>();

        foreach (var component in RazorComponents)
        {
            if (!string.Equals(component.GetMetadata("RazorInlineCssProof"), "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = component.GetMetadata("Identity");
            var outputPath = Path.Combine(OutputDirectory, relativePath + ".css");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            WriteIfChanged(outputPath, ProofCss + Environment.NewLine);

            var item = new TaskItem(outputPath);
            item.SetMetadata("RazorComponent", component.ItemSpec);
            generated.Add(item);
        }

        GeneratedCss = [.. generated];
        return !Log.HasLoggedErrors;
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content);
    }
}
