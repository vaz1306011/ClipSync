using System.Text;
using System.Text.RegularExpressions;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Pulls the one-time code out of an SMS body. Deliberately conservative: a
/// message that doesn't look like a verification message yields nothing, since
/// the client forwards whatever its coarse message filter matched.
/// </summary>
internal static class OtpExtractor
{
    private static readonly Regex UrlRegex = new(@"https?://[\x21-\x7E]+", RegexOptions.IgnoreCase);

    // The origin-bound format ("@example.com #123456") names the code outright.
    private static readonly Regex OriginBoundRegex = new(@"@[A-Za-z0-9.\-]+ #([0-9]{4,8})(?![0-9])");

    // \b treats kana and kanji as word characters, so the English words are
    // delimited by "no Latin letter on either side" instead.
    public const string DefaultKeywords = "認証\n認證\n驗證\n验证\n確認コード\nワンタイム\nパスコード\nセキュリティコード\ncode\notp\npasscode\nverification";

    private static Regex CreateCustomPattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    // 4–8 digits that aren't part of a phone number, date, time or amount.
    private static readonly Regex CodeRegex = new(
        @"(?<![0-9¥$]|[0-9][\-./,:])[0-9]{4,8}(?![0-9]|[\-./,:][0-9]|\s*[円年月])");

    public static string Prepare(string message, AppConfig config)
    {
        var text = config.NormalizeOtp ? message.Normalize(NormalizationForm.FormKC) : message;
        return config.ExcludeOtpUrls ? UrlRegex.Replace(text, " ") : text;
    }

    public static string? Extract(string message, AppConfig config)
    {
        var text = Prepare(message, config);
        if (ExtractByRules(text, config.OtpPattern) is { } custom)
            return custom.Code;

        var originBound = OriginBoundRegex.Match(text);

        if (originBound.Success)
        {
            return originBound.Groups[1].Value;
        }

        var terms = (config.OtpKeywords ?? DefaultKeywords)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.All(char.IsAsciiLetter)
                ? $"(?<![A-Za-z]){Regex.Escape(term)}(?![A-Za-z])"
                : Regex.Escape(term))
            .ToArray();
        if (terms.Length == 0)
            return null;

        var keywords = Regex.Matches(text, string.Join("|", terms), RegexOptions.IgnoreCase);

        if (keywords.Count == 0)
        {
            return null;
        }

        return CodeRegex.Matches(text)
            .OrderBy(code => keywords.Min(keyword => Distance(code, keyword)))
            .FirstOrDefault()
            ?.Value;
    }

    /// <summary>
    /// Tries the custom patterns, one per line, from the top. The first that
    /// yields a code wins; an invalid or slow one is reported and skipped so it
    /// blocks neither the later patterns nor the default detector.
    /// </summary>
    public static (int Line, Match Match, string Code)? ExtractByRules(
        string text, string? patterns, Action<int, Exception>? onError = null)
    {
        foreach (var (line, pattern) in SplitPatterns(patterns))
        {
            try
            {
                var match = CreateCustomPattern(pattern).Match(text);
                if (ExtractCustom(match, text) is { } code)
                    return (line, match, code);
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                onError?.Invoke(line, ex);
            }
        }

        return null;
    }

    /// <summary>Throws for the first line that isn't a valid regular expression.</summary>
    public static void ValidatePatterns(string? patterns)
    {
        foreach (var (line, pattern) in SplitPatterns(patterns))
        {
            try
            {
                CreateCustomPattern(pattern);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"正規表現の {line} 行目が正しくありません。\n{ex.Message}", ex);
            }
        }
    }

    // Line numbers count blank lines, so they match what the settings box shows.
    private static IEnumerable<(int Line, string Pattern)> SplitPatterns(string? patterns) =>
        (patterns ?? "")
            .Split('\n')
            .Select((pattern, index) => (Line: index + 1, Pattern: pattern.Trim()))
            .Where(rule => rule.Pattern.Length != 0);

    /// <summary>
    /// The code a custom pattern's match names: the group "code", else the first
    /// group if it holds a digit, else the first ordinary code inside the match.
    /// </summary>
    private static string? ExtractCustom(Match match, string text)
    {
        if (!match.Success)
            return null;

        if (match.Groups["code"] is { Success: true, Length: > 0 } named)
            return named.Value;

        if (match.Groups.Count > 1 && match.Groups[1].Value is var first && first.Any(char.IsDigit))
            return first;

        // Searched in the whole text, so that what surrounds the digits still
        // tells phone numbers, dates and amounts apart.
        return CodeRegex.Matches(text)
            .FirstOrDefault(code => code.Index >= match.Index && code.Index + code.Length <= match.Index + match.Length)
            ?.Value;
    }

    private static int Distance(Match code, Match keyword) =>
        code.Index >= keyword.Index
            ? code.Index - (keyword.Index + keyword.Length)
            : keyword.Index - (code.Index + code.Length);
}
