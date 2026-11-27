using System.Security.Cryptography;
using System.Text;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// The client keeps no record of what it has sent: it posts every recent message
/// each time that set changes. This splits such a batch and yields the codes of
/// the messages not seen before.
/// </summary>
internal sealed class OtpInbox
{
    /// <summary>Joins the messages in one request body. Must match the shortcut.</summary>
    public const string Separator = "<<ClipSync:next>>";

    // Longer than the window the client sends (10 minutes), so a message can't be
    // forgotten while it may still be sent again.
    private static readonly TimeSpan Memory = TimeSpan.FromMinutes(15);

    private readonly object _lock = new();

    // Hashes only; message bodies aren't kept.
    private readonly Dictionary<string, DateTimeOffset> _seen = new();

    /// <param name="body">Messages newest first, joined by <see cref="Separator"/>.</param>
    /// <returns>The new codes, oldest first, so the newest is announced last.</returns>
    public IReadOnlyList<string> TakeNewCodes(string body, DateTimeOffset now, AppConfig config)
    {
        var codes = new List<string>();

        lock (_lock)
        {
            foreach (var expired in _seen.Where(entry => now - entry.Value > Memory).Select(entry => entry.Key).ToList())
            {
                _seen.Remove(expired);
            }

            var messages = body.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var message in messages.Reverse())
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
                var isNew = !_seen.ContainsKey(hash);
                _seen[hash] = now;

                if (isNew && OtpExtractor.Extract(message, config) is { } code)
                {
                    codes.Add(code);
                }
            }
        }

        return codes;
    }
}
