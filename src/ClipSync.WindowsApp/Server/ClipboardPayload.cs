namespace ClipSync.WindowsApp.Server;

internal enum ClipboardContentType
{
    Text,
    File
}

/// <summary>
/// One clipboard snapshot — either plain text, or a file/image carried as raw
/// bytes (images and arbitrary files are both just "a named blob" here).
/// </summary>
internal sealed record ClipboardPayload(
    ClipboardContentType Type,
    string? Text,
    string? FileName,
    string? MimeType,
    byte[]? Data,
    DateTimeOffset UpdatedAt)
{
    public static ClipboardPayload ForText(string text) =>
        new(ClipboardContentType.Text, text, null, null, null, DateTimeOffset.UtcNow);

    public static ClipboardPayload ForFile(string fileName, string mimeType, byte[] data) =>
        new(ClipboardContentType.File, null, fileName, mimeType, data, DateTimeOffset.UtcNow);
}
