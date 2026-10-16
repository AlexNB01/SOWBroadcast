using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace SowBroadcast.App.Converters;

/// <summary>Image file path -> small preview bitmap (null when the file is missing or unreadable).</summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0 || !File.Exists(path)) return null;
        var width = parameter is string p && int.TryParse(p, out var w) ? w : 360;
        try
        {
            using var stream = File.OpenRead(path);
            // Logos are often thousands of pixels wide; decode them down to preview size.
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
