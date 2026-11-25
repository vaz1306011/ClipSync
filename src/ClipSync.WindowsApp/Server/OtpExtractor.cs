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
    private static readonly Regex KeywordRegex = new(
        "認証|認證|驗證|验证|確認コード|ワンタイム|パスコード|セキュリティコード"
        + "|(?<![A-Za-z])(?:code|otp|passcode|verification)(?![A-Za-z])",
        RegexOptions.IgnoreCase);

    // 4–8 digits that aren't part of a phone number, date, time or amount.
    private static readonly Regex CodeRegex = new(
        @"(?<![0-9¥$]|[0-9][\-./,:])[0-9]{4,8}(?![0-9]|[\-./,:][0-9]|\s*[円年月])");

    public static string? Extract(string message)
    {
        // NFKC folds full-width digits and punctuation into their ASCII forms.
        var text = UrlRegex.Replace(message.Normalize(NormalizationForm.FormKC), " ");

        var originBound = OriginBoundRegex.Match(text);

        if (originBound.Success)
        {
            return originBound.Groups[1].Value;
        }

        var keywords = KeywordRegex.Matches(text);

        if (keywords.Count == 0)
        {
            return null;
        }

        return CodeRegex.Matches(text)
            .OrderBy(code => keywords.Min(keyword => Distance(code, keyword)))
            .FirstOrDefault()
            ?.Value;
    }

    private static int Distance(Match code, Match keyword) =>
        code.Index >= keyword.Index
            ? code.Index - (keyword.Index + keyword.Length)
            : keyword.Index - (code.Index + code.Length);
}
