using System.Text;
using System.Xml.Linq;
using BrawlhallaSwz.Xml;

namespace Brawlhalalla;

public sealed record XmlChange(string Tag, string OldValue, string NewValue, string Context);

/// <summary>
/// Tag-anchored editing of Brawlhalla's XML entries.
///
/// Edits are located by walking the markup and matching whole element text, never by blind
/// find/replace — so renaming "Cross" cannot touch "crossover", and an element listed as protected
/// (an internal ID like &lt;LegendName&gt;) is never written to even when its value matches.
///
/// Splicing is done on the original string so that every byte we did not deliberately change stays
/// identical. The result is then re-parsed with BhXmlParser — the same Haxe parser Brawlhalla uses —
/// and structurally compared against the original, so a malformed edit is caught before it can reach
/// an archive.
/// </summary>
public static class XmlEdit
{
    /// <summary>
    /// Rewrites leaf-element text. <paramref name="transform"/> receives the tag name and the
    /// decoded text and returns replacement text, or null to leave the element alone.
    /// </summary>
    public static string Rewrite(
        string xml,
        IReadOnlySet<string> tags,
        Func<string, string, string?> transform,
        List<XmlChange> changes)
    {
        StringBuilder output = new(xml.Length);
        int copied = 0;
        int i = 0;
        string context = "";

        while (i < xml.Length)
        {
            if (xml[i] != '<') { i++; continue; }

            if (Matches(xml, i, "<!--")) { i = SkipTo(xml, i + 4, "-->", 3); continue; }
            if (Matches(xml, i, "<![CDATA[")) { i = SkipTo(xml, i + 9, "]]>", 3); continue; }
            if (i + 1 >= xml.Length || (!char.IsLetter(xml[i + 1]) && xml[i + 1] != '_')) { i++; continue; }

            int nameStart = i + 1;
            int nameEnd = nameStart;
            while (nameEnd < xml.Length && (char.IsLetterOrDigit(xml[nameEnd]) || xml[nameEnd] is '_' or '-' or ':' or '.'))
                nameEnd++;
            string tagName = xml[nameStart..nameEnd];

            int openEnd = FindOpenTagEnd(xml, nameEnd);
            if (openEnd < 0) break;

            bool selfClosing = openEnd > 0 && xml[openEnd - 1] == '/';
            if (selfClosing || !tags.Contains(tagName))
            {
                // Track the nearest enclosing identifiable element so changes can be reported with
                // something more useful than a tag name.
                if (!selfClosing && IsContextTag(tagName)) context = tagName;
                i = openEnd + 1;
                continue;
            }

            int closeStart = FindCloseTag(xml, openEnd + 1, tagName);
            if (closeStart < 0) { i = openEnd + 1; continue; }

            string inner = xml[(openEnd + 1)..closeStart];

            // Only leaf text is editable. An element with child elements is left untouched.
            if (inner.Contains('<'))
            {
                i = openEnd + 1;
                continue;
            }

            string trimmed = inner.Trim();
            string decoded = Unescape(trimmed);
            string? replacement = transform(tagName, decoded);

            if (replacement is not null && replacement != decoded)
            {
                int leading = inner.Length - inner.TrimStart().Length;
                int trailing = inner.Length - inner.TrimEnd().Length;

                output.Append(xml, copied, openEnd + 1 - copied);
                output.Append(inner, 0, leading);
                output.Append(Escape(replacement));
                output.Append(inner, inner.Length - trailing, trailing);
                copied = closeStart;

                changes.Add(new XmlChange(tagName, decoded, replacement, context));
            }

            i = closeStart;
        }

        if (changes.Count == 0) return xml;

        output.Append(xml, copied, xml.Length - copied);
        string result = output.ToString();

        Validate(xml, result, changes.Count);
        return result;
    }

    /// <summary>Collects the decoded text of every occurrence of a leaf tag.</summary>
    public static List<string> CollectValues(string xml, string tag)
    {
        List<string> values = [];
        Rewrite(xml, new HashSet<string>(StringComparer.Ordinal) { tag }, (_, value) =>
        {
            values.Add(value);
            return null;
        }, []);
        return values;
    }

    /// <summary>Every distinct element name in the document, for diagnostics when a pass matches nothing.</summary>
    public static SortedSet<string> ElementNames(string xml)
    {
        SortedSet<string> names = new(StringComparer.Ordinal);
        int i = 0;
        while (i < xml.Length)
        {
            if (xml[i] != '<') { i++; continue; }
            if (Matches(xml, i, "<!--")) { i = SkipTo(xml, i + 4, "-->", 3); continue; }
            if (Matches(xml, i, "<![CDATA[")) { i = SkipTo(xml, i + 9, "]]>", 3); continue; }
            if (i + 1 >= xml.Length || !char.IsLetter(xml[i + 1])) { i++; continue; }

            int start = i + 1, end = i + 1;
            while (end < xml.Length && (char.IsLetterOrDigit(xml[end]) || xml[end] is '_' or '-' or ':' or '.')) end++;
            names.Add(xml[start..end]);
            i = end;
        }
        return names;
    }

    /// <summary>
    /// Re-parses the edited document with the game's own parser and asserts nothing changed except
    /// the intended text values.
    /// </summary>
    private static void Validate(string original, string edited, int expectedChanges)
    {
        XDocument before, after;
        try
        {
            before = BhXmlParser.Parse(original);
            after = BhXmlParser.Parse(edited);
        }
        catch (Exception ex)
        {
            throw new EditValidationException($"Edited XML no longer parses with Brawlhalla's parser: {ex.Message}", ex);
        }

        List<string> textDiffs = [];
        CompareElements(before.Root, after.Root, "", textDiffs);

        if (textDiffs.Count != expectedChanges)
        {
            throw new EditValidationException(
                $"Edit verification failed: expected exactly {expectedChanges} changed text value(s), " +
                $"but the re-parsed document differs in {textDiffs.Count} place(s): {string.Join(", ", textDiffs.Take(5))}");
        }
    }

    private static void CompareElements(XElement? a, XElement? b, string path, List<string> textDiffs)
    {
        if (a is null || b is null)
        {
            if (!ReferenceEquals(a, b)) throw new EditValidationException($"Element presence changed at {path}.");
            return;
        }

        if (a.Name.LocalName != b.Name.LocalName)
            throw new EditValidationException($"Element name changed at {path}: {a.Name.LocalName} -> {b.Name.LocalName}");

        string here = path.Length == 0 ? a.Name.LocalName : $"{path}/{a.Name.LocalName}";

        List<XAttribute> aAttrs = [.. a.Attributes()];
        List<XAttribute> bAttrs = [.. b.Attributes()];
        if (aAttrs.Count != bAttrs.Count)
            throw new EditValidationException($"Attribute count changed at {here}.");
        for (int i = 0; i < aAttrs.Count; i++)
        {
            if (aAttrs[i].Name.LocalName != bAttrs[i].Name.LocalName || aAttrs[i].Value != bAttrs[i].Value)
                throw new EditValidationException($"Attribute changed at {here}: {aAttrs[i].Name.LocalName}");
        }

        if (DirectText(a) != DirectText(b))
            textDiffs.Add(here);

        List<XElement> aKids = [.. a.Elements()];
        List<XElement> bKids = [.. b.Elements()];
        if (aKids.Count != bKids.Count)
            throw new EditValidationException($"Child element count changed at {here}: {aKids.Count} -> {bKids.Count}");

        for (int i = 0; i < aKids.Count; i++)
            CompareElements(aKids[i], bKids[i], here, textDiffs);
    }

    private static string DirectText(XElement e)
    {
        StringBuilder sb = new();
        foreach (XNode node in e.Nodes())
        {
            if (node is XText text) sb.Append(text.Value);
            else if (node is XCData cdata) sb.Append(cdata.Value);
        }
        return sb.ToString().Trim();
    }

    // --- scanning helpers ---

    private static bool IsContextTag(string tag) =>
        tag is "LegendType" or "LevelType" or "PlaylistType" or "ItemType" or "CostumeType";

    private static bool Matches(string s, int i, string token) =>
        i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

    private static int SkipTo(string s, int from, string token, int tokenLength)
    {
        int idx = s.IndexOf(token, from, StringComparison.Ordinal);
        return idx < 0 ? s.Length : idx + tokenLength;
    }

    /// <summary>Index of the '&gt;' ending an open tag, skipping quoted attribute values.</summary>
    private static int FindOpenTagEnd(string s, int from)
    {
        char quote = '\0';
        for (int i = from; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '>') return i;
        }
        return -1;
    }

    /// <summary>Index of the '&lt;' starting the matching close tag, skipping CDATA and comments.</summary>
    private static int FindCloseTag(string s, int from, string tagName)
    {
        string close = "</" + tagName;
        int i = from;
        while (i < s.Length)
        {
            if (Matches(s, i, "<!--")) { i = SkipTo(s, i + 4, "-->", 3); continue; }
            if (Matches(s, i, "<![CDATA[")) { i = SkipTo(s, i + 9, "]]>", 3); continue; }
            if (Matches(s, i, close))
            {
                int after = i + close.Length;
                if (after < s.Length && (s[after] == '>' || char.IsWhiteSpace(s[after]))) return i;
            }
            i++;
        }
        return -1;
    }

    // --- entity handling, mirroring BhXmlParser / BhXmlPrinter ---

    public static string Escape(string s)
    {
        if (s.AsSpan().IndexOfAny('&', '<', '>') < 0) return s;
        StringBuilder sb = new(s.Length + 8);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        if (!s.Contains('&')) return s;
        StringBuilder sb = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '&') { sb.Append(s[i]); continue; }
            int end = s.IndexOf(';', i + 1);
            if (end < 0 || end - i > 10) { sb.Append(s[i]); continue; }

            string entity = s[(i + 1)..end];
            string? decoded = entity switch
            {
                "lt" => "<",
                "gt" => ">",
                "amp" => "&",
                "quot" => "\"",
                "apos" => "'",
                _ => null,
            };

            if (decoded is null && entity.Length > 1 && entity[0] == '#')
            {
                string digits = entity[1..];
                bool hex = digits.Length > 0 && (digits[0] == 'x' || digits[0] == 'X');
                if (hex) digits = digits[1..];
                if (int.TryParse(digits,
                        hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int code) && code is > 0 and <= 0x10FFFF)
                {
                    decoded = char.ConvertFromUtf32(code);
                }
            }

            if (decoded is null) { sb.Append(s[i]); continue; }
            sb.Append(decoded);
            i = end;
        }
        return sb.ToString();
    }
}

public sealed class EditValidationException : Exception
{
    public EditValidationException(string message) : base(message) { }
    public EditValidationException(string message, Exception inner) : base(message, inner) { }
}
