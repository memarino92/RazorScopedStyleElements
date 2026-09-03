using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace RazorInlineCss.Tasks;

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
            var openingStart = source.IndexOf("<style>", StringComparison.OrdinalIgnoreCase);
            if (openingStart < 0)
            {
                continue;
            }

            var contentStart = openingStart + "<style>".Length;
            var closingStart = source.IndexOf("</style>", contentStart, StringComparison.OrdinalIgnoreCase);
            if (closingStart < 0)
            {
                Log.LogError($"Inline style in '{component.ItemSpec}' does not have a closing </style> element.");
                continue;
            }

            var blockEnd = closingStart + "</style>".Length;
            var relativePath = GetRelativePath(component);
            var razorPath = Path.Combine(OutputDirectory, "razor", relativePath);
            var cssPath = Path.Combine(OutputDirectory, "css", relativePath + ".css");

            var transformedSource = PreserveLinesWhileRemoving(source, openingStart, blockEnd - openingStart);
            WriteIfChanged(razorPath, transformedSource);
            WriteIfChanged(cssPath, source[contentStart..closingStart].Trim() + Environment.NewLine);

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

    private static string PreserveLinesWhileRemoving(string source, int start, int length)
    {
        var removed = source.AsSpan(start, length);
        var replacement = new string(removed.ToArray().Select(character => character is '\r' or '\n' ? character : ' ').ToArray());
        return string.Concat(source.AsSpan(0, start), replacement, source.AsSpan(start + length));
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
