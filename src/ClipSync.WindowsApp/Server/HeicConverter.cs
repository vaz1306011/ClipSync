using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipSync.WindowsApp.Server;

/// <summary>
/// Converts HEIC/HEIF bytes to PNG with the Windows imaging stack (WIC). Needs
/// the "HEIF Image Extensions" codec; without it decoding fails and callers
/// keep the original bytes.
/// </summary>
internal static class HeicConverter
{
    public static async Task<byte[]?> TryConvertToPngAsync(byte[] heicBytes)
    {
        try
        {
            using var input = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(input))
            {
                writer.WriteBytes(heicBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            var png = new byte[output.Size];
            output.Seek(0);
            using var reader = new DataReader(output);
            await reader.LoadAsync((uint)output.Size);
            reader.ReadBytes(png);
            return png;
        }
        catch (Exception)
        {
            // Codec missing or file unreadable — fall back to the original HEIC.
            return null;
        }
    }
}
