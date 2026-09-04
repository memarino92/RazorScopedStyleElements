namespace RazorScopedStyleElements.Tasks;

/// <summary>
/// Performs a bounded structural scan of Razor markup without taking a dependency on SDK-internal compiler binaries.
/// It tracks HTML depth and skips balanced Razor/C# regions, strings, and comments; unsupported ambiguity is diagnosed
/// instead of being interpreted heuristically.
/// </summary>
public static class InlineStyleExtractor
{
    private static readonly HashSet<string> CssAtRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "charset", "container", "counter-style", "document", "font-face", "font-feature-values",
        "font-palette-values", "import", "keyframes", "layer", "media", "namespace", "page",
        "property", "scope", "starting-style", "supports", "view-transition",
    };

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr",
    };

    public static InlineStyleExtraction Extract(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<InlineStyleDiagnostic>();
        var styles = new List<StyleSpan>();
        var elements = new Stack<string>();

        for (var index = 0; index < source.Length;)
        {
            if (StartsWith(source, index, "@*"))
            {
                index = SkipDelimited(source, index, "*@", diagnostics, "Unterminated Razor comment.");
                continue;
            }

            if (StartsWith(source, index, "<!--"))
            {
                index = SkipDelimited(source, index, "-->", diagnostics, "Unterminated HTML comment.");
                continue;
            }

            if (source[index] == '@')
            {
                index = SkipRazorConstruct(source, index, diagnostics);
                continue;
            }

            if (source[index] != '<' || !TryReadTag(source, index, out var tag))
            {
                index++;
                continue;
            }

            if (tag.IsClosing)
            {
                if (elements.Count > 0 && string.Equals(elements.Peek(), tag.Name, StringComparison.OrdinalIgnoreCase))
                {
                    elements.Pop();
                }

                index = tag.End;
                continue;
            }

            if (!string.Equals(tag.Name, "style", StringComparison.OrdinalIgnoreCase))
            {
                if (!tag.IsSelfClosing && !VoidElements.Contains(tag.Name))
                {
                    elements.Push(tag.Name);
                }

                index = tag.End;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tag.Attributes))
            {
                diagnostics.Add(CreateDiagnostic(source, "RSSE003", "Inline <style> elements cannot have attributes.", index));
            }

            var closingStart = source.IndexOf("</style>", tag.End, StringComparison.OrdinalIgnoreCase);
            if (closingStart < 0)
            {
                diagnostics.Add(CreateDiagnostic(source, "RSSE003", "Inline <style> element is missing its closing tag.", index));
                break;
            }

            if (elements.Count > 0)
            {
                diagnostics.Add(CreateDiagnostic(source, "RSSE002", "Inline <style> elements must be top-level.", index));
            }

            var css = source[tag.End..closingStart];
            var dynamicOffset = FindDynamicRazorTransition(css);
            if (dynamicOffset >= 0)
            {
                diagnostics.Add(CreateDiagnostic(source, "RSSE003", "Inline CSS cannot contain runtime Razor expressions.", tag.End + dynamicOffset));
            }

            styles.Add(new StyleSpan(index, closingStart + "</style>".Length, tag.End, closingStart));
            index = closingStart + "</style>".Length;
        }

        if (styles.Count > 1)
        {
            diagnostics.Add(CreateDiagnostic(source, "RSSE001", "A Razor component can contain only one inline <style> element.", styles[1].Start));
        }

        if (styles.Count == 0 && !diagnostics.Any(diagnostic => diagnostic.Message.Contains("<style>", StringComparison.Ordinal)))
        {
            diagnostics.Clear();
        }

        if (styles.Count != 1 || diagnostics.Count > 0)
        {
            return new InlineStyleExtraction(source, null, diagnostics);
        }

        var style = styles[0];
        var transformed = source.ToCharArray();
        for (var index = style.Start; index < style.End; index++)
        {
            if (transformed[index] is not ('\r' or '\n'))
            {
                transformed[index] = ' ';
            }
        }

        return new InlineStyleExtraction(
            new string(transformed),
            source[style.ContentStart..style.ContentEnd].Trim() + Environment.NewLine,
            diagnostics);
    }

    private static int SkipRazorConstruct(string source, int start, List<InlineStyleDiagnostic> diagnostics)
    {
        if (start + 1 >= source.Length)
        {
            return source.Length;
        }

        if (source[start + 1] == '@')
        {
            return start + 2;
        }

        var index = start + 1;
        if (source[index] == '(')
        {
            return SkipBalancedCode(source, index, '(', ')', diagnostics);
        }

        if (!IsIdentifierStart(source[index]))
        {
            return index;
        }

        while (index < source.Length && IsIdentifierPart(source[index]))
        {
            index++;
        }

        index = SkipWhitespace(source, index);
        if (index < source.Length && source[index] == '(')
        {
            index = SkipBalancedCode(source, index, '(', ')', diagnostics);
            index = SkipWhitespace(source, index);
        }

        if (index < source.Length && source[index] == '{')
        {
            return SkipBalancedCode(source, index, '{', '}', diagnostics);
        }

        return index;
    }

    private static int SkipBalancedCode(string source, int start, char opening, char closing, List<InlineStyleDiagnostic> diagnostics)
    {
        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            if (StartsWith(source, index, "//"))
            {
                var newline = source.IndexOf('\n', index + 2);
                index = newline < 0 ? source.Length - 1 : newline;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    diagnostics.Add(CreateDiagnostic(source, "RSSE003", "Unterminated Razor code comment.", index));
                    return source.Length;
                }

                index = end + 1;
                continue;
            }

            if (source[index] is '\'' or '"')
            {
                index = SkipQuoted(source, index);
                continue;
            }

            if (source[index] == '<' && TryReadTag(source, index, out var tag) && string.Equals(tag.Name, "style", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(CreateDiagnostic(source, "RSSE002", "Inline <style> elements cannot be conditional or inside Razor code.", index));
            }

            if (source[index] == opening)
            {
                depth++;
            }
            else if (source[index] == closing && --depth == 0)
            {
                return index + 1;
            }
        }

        diagnostics.Add(CreateDiagnostic(source, "RSSE003", "Unterminated Razor code block or expression.", start));
        return source.Length;
    }

    private static int FindDynamicRazorTransition(string css)
    {
        for (var index = 0; index < css.Length; index++)
        {
            if (StartsWith(css, index, "/*"))
            {
                var end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 1;
                continue;
            }

            if (css[index] is '\'' or '"')
            {
                index = SkipQuoted(css, index);
                continue;
            }

            if (css[index] != '@')
            {
                continue;
            }

            var nameStart = index + 1;
            var nameEnd = nameStart;
            while (nameEnd < css.Length && (char.IsLetter(css[nameEnd]) || css[nameEnd] == '-'))
            {
                nameEnd++;
            }

            if (nameEnd == nameStart || !CssAtRules.Contains(css[nameStart..nameEnd]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryReadTag(string source, int start, out Tag tag)
    {
        tag = default;
        var index = start + 1;
        var closing = index < source.Length && source[index] == '/';
        if (closing)
        {
            index++;
        }

        if (index >= source.Length || !IsIdentifierStart(source[index]))
        {
            return false;
        }

        var nameStart = index;
        while (index < source.Length && (IsIdentifierPart(source[index]) || source[index] is '-' or ':' or '.'))
        {
            index++;
        }

        var name = source[nameStart..index];
        var attributesStart = index;
        char quote = '\0';
        while (index < source.Length)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '>')
            {
                var attributesEnd = index;
                while (attributesEnd > attributesStart && char.IsWhiteSpace(source[attributesEnd - 1]))
                {
                    attributesEnd--;
                }

                var selfClosing = attributesEnd > attributesStart && source[attributesEnd - 1] == '/';
                if (selfClosing)
                {
                    attributesEnd--;
                }

                tag = new Tag(name, closing, selfClosing, source[attributesStart..attributesEnd], index + 1);
                return true;
            }

            index++;
        }

        return false;
    }

    private static int SkipDelimited(string source, int start, string terminator, List<InlineStyleDiagnostic> diagnostics, string message)
    {
        var end = source.IndexOf(terminator, start + 2, StringComparison.Ordinal);
        if (end >= 0)
        {
            return end + terminator.Length;
        }

        diagnostics.Add(CreateDiagnostic(source, "RSSE003", message, start));
        return source.Length;
    }

    private static int SkipQuoted(string text, int start)
    {
        var quote = text[start];
        for (var index = start + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
            }
            else if (text[index] == quote)
            {
                return index;
            }
        }

        return text.Length - 1;
    }

    private static InlineStyleDiagnostic CreateDiagnostic(string source, string id, string message, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new InlineStyleDiagnostic(id, message, offset, line, column);
    }

    private static int SkipWhitespace(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index;
    }

    private static bool StartsWith(string source, int index, string value) =>
        source.AsSpan(index).StartsWith(value, StringComparison.Ordinal);

    private static bool IsIdentifierStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    private readonly record struct StyleSpan(int Start, int End, int ContentStart, int ContentEnd);

    private readonly record struct Tag(string Name, bool IsClosing, bool IsSelfClosing, string Attributes, int End);
}

public sealed record InlineStyleExtraction(
    string TransformedSource,
    string? Css,
    IReadOnlyList<InlineStyleDiagnostic> Diagnostics)
{
    public bool HasStyle => Css is not null;
}

public sealed record InlineStyleDiagnostic(string Id, string Message, int Offset, int Line, int Column);
