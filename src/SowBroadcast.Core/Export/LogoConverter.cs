using SkiaSharp;

namespace SowBroadcast.Core.Export;

/// <summary>Turns a user-picked logo (png/jpg/webp...) into PNG bytes for the overlay tree.</summary>
public interface ILogoConverter
{
    /// <summary>Returns PNG bytes, or null when the file is missing or cannot be decoded.</summary>
    byte[]? ToPng(string? sourcePath);
}

public sealed class SkiaLogoConverter : ILogoConverter
{
    public byte[]? ToPng(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
        try
        {
            using var bitmap = SKBitmap.Decode(sourcePath);
            if (bitmap is null) return null;
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
