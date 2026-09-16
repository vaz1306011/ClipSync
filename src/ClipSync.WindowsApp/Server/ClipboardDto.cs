namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Wire format for /clipboard and /clipboard/latest. `Data` round-trips as a
/// Base64 string automatically — System.Text.Json does that for byte[] with
/// no extra code, and it's exactly what a Shortcuts "Base64 Decode" expects.
/// </summary>
internal sealed record ClipboardDto(
    string Type,
    string? Text,
    string? FileName,
    string? MimeType,
    byte[]? Data,
    DateTimeOffset UpdatedAt)
{
    public static ClipboardDto From(ClipboardPayload payload) => new(
        payload.Type == ClipboardContentType.File ? "file" : "text",
        payload.Text,
        payload.FileName,
        payload.MimeType,
        payload.Data,
        payload.UpdatedAt);

    public ClipboardPayload ToPayload() =>
        Type == "file"
            ? ClipboardPayload.ForFile(FileName ?? "file", MimeType ?? "application/octet-stream", Data ?? [])
            : ClipboardPayload.ForText(Text ?? string.Empty);
}
