using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace RazorScopedStyleElements.Tasks;

public sealed class TransformRazorComponents : Microsoft.Build.Utilities.Task
{
    [Required]
    public required ITaskItem[] RazorComponents { get; init; }

    [Required]
    public required string ProjectDirectory { get; init; }

    [Required]
    public required string OutputDirectory { get; init; }

    [Output]
    public ITaskItem[] OriginalComponents { get; private set; } = [];

    [Output]
    public ITaskItem[] TransformedComponents { get; private set; } = [];

    [Output]
    public ITaskItem[] GeneratedCss { get; private set; } = [];

    public override bool Execute()
    {
        var originals = new List<ITaskItem>();
        var transformedComponents = new List<ITaskItem>();
        var generatedCss = new List<ITaskItem>();

        foreach (var component in RazorComponents)
        {
            var source = File.ReadAllText(component.GetMetadata("FullPath"));
            var extraction = InlineStyleExtractor.Extract(source);
            foreach (var diagnostic in extraction.Diagnostics)
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: diagnostic.Id,
                    helpKeyword: null,
                    file: component.GetMetadata("FullPath"),
                    lineNumber: diagnostic.Line,
                    columnNumber: diagnostic.Column,
                    endLineNumber: diagnostic.Line,
                    endColumnNumber: diagnostic.Column + 1,
                    message: diagnostic.Message);
            }

            if (!extraction.HasStyle)
            {
                continue;
            }

            if (File.Exists(component.GetMetadata("FullPath") + ".css"))
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: "RICSS004",
                    helpKeyword: null,
                    file: component.GetMetadata("FullPath"),
                    lineNumber: 1,
                    columnNumber: 1,
                    endLineNumber: 1,
                    endColumnNumber: 2,
                    message: "A component cannot use both inline CSS and a sibling .razor.css file.");
                continue;
            }

            var relativePath = GetRelativePath(component);
            var razorPath = Path.Combine(OutputDirectory, "razor", relativePath);
            var cssPath = Path.Combine(OutputDirectory, "css", relativePath + ".css");

            WriteIfChanged(razorPath, extraction.TransformedSource);
            WriteIfChanged(cssPath, extraction.Css!);

            var transformed = new TaskItem(razorPath);
            component.CopyMetadataTo(transformed);
            transformed.SetMetadata("TargetPath", relativePath);
            transformed.SetMetadata("OriginalComponent", component.ItemSpec);

            var css = new TaskItem(cssPath);
            css.SetMetadata("RazorComponent", transformed.ItemSpec);

            originals.Add(component);
            transformedComponents.Add(transformed);
            generatedCss.Add(css);
        }

        OriginalComponents = [.. originals];
        TransformedComponents = [.. transformedComponents];
        GeneratedCss = [.. generatedCss];
        return !Log.HasLoggedErrors;
    }

    private string GetRelativePath(ITaskItem component)
    {
        var itemSpec = component.ItemSpec;
        var relativePath = Path.IsPathRooted(itemSpec)
            ? Path.GetRelativePath(ProjectDirectory, itemSpec)
            : itemSpec;

        if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Razor component '{itemSpec}' is outside the project directory.");
        }

        return relativePath;
    }

    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content);
    }
}
