using System.Globalization;
using System.Text;

namespace GnomeWin.Services.Search;

public static class TextMatcher
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        string d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static int Score(string query, string candidate)
    {
        if (query.Length == 0 || candidate.Length == 0) return 0;
        if (candidate == query) return 1000;
        if (candidate.StartsWith(query, StringComparison.Ordinal)) return 900 - Math.Min(candidate.Length, 100);

        int wordIdx = IndexOfWordStart(candidate, query);
        if (wordIdx > 0) return 800 - Math.Min(wordIdx, 100);

        if (query.Length >= 2 && MatchesInitials(query, candidate)) return 700;

        int idx = candidate.IndexOf(query, StringComparison.Ordinal);
        if (idx >= 0) return 600 - Math.Min(idx, 100);

        if (query.Length >= 3)
        {
            int gaps = SubsequenceGaps(query, candidate);
            if (gaps >= 0 && gaps <= query.Length) return Math.Max(1, 300 - gaps * 20);
        }
        return 0;
    }

    private static int IndexOfWordStart(string text, string query)
    {
        int i = 0;
        while ((i = text.IndexOf(query, i + 1, StringComparison.Ordinal)) > 0)
        {
            char prev = text[i - 1];
            if (!char.IsLetterOrDigit(prev)) return i;
        }
        return -1;
    }

    private static bool MatchesInitials(string query, string text)
    {
        var initials = new StringBuilder();
        bool atStart = true;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) { if (atStart) initials.Append(c); atStart = false; }
            else atStart = true;
        }
        return initials.ToString().StartsWith(query, StringComparison.Ordinal);
    }

    private static int SubsequenceGaps(string query, string text)
    {
        int best = -1;
        for (int start = text.IndexOf(query[0]); start >= 0; start = text.IndexOf(query[0], start + 1))
        {
            int qi = 1, gaps = 0, last = start;
            for (int i = start + 1; i < text.Length && qi < query.Length; i++)
            {
                if (text[i] != query[qi]) continue;
                gaps += i - last - 1;
                last = i;
                qi++;
            }
            if (qi == query.Length && (best < 0 || gaps < best)) best = gaps;
        }
        return best;
    }
}
