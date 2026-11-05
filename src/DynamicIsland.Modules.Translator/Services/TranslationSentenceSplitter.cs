namespace DynamicIsland.Modules.Translator.Services;

/// <summary>Conservative boundaries for sentence-trained models; decimals and initials stay together.</summary>
internal static class TranslationSentenceSplitter
{
    internal static string[] Split(string text)
    {
        var result = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '\n')) continue;
            var end = i + 1;
            while (end < text.Length && text[end] is '.' or '!' or '?' or '"' or '\'' or '»' or '”') end++;
            if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
            var next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            if (next >= text.Length) break;
            if (text[i] == '.' && (!char.IsUpper(text[next]) || IsAbbreviation(text, i))) continue;
            var segment = text[start..end].Trim();
            if (segment.Length > 0) result.Add(segment);
            start = next;
            i = next - 1;
        }
        var remaining = text[start..].Trim();
        if (remaining.Length > 0) result.Add(remaining);
        return result.ToArray();
    }

    private static bool IsAbbreviation(string text, int dot)
    {
        var start = dot - 1;
        while (start >= 0 && char.IsLetter(text[start])) start--;
        var word = text[(start + 1)..dot];
        return word.Length == 1 || word.Equals("Mr", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("Mrs", StringComparison.OrdinalIgnoreCase) || word.Equals("Ms", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("Dr", StringComparison.OrdinalIgnoreCase) || word.Equals("Prof", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("г", StringComparison.OrdinalIgnoreCase) || word.Equals("ул", StringComparison.OrdinalIgnoreCase);
    }
}
