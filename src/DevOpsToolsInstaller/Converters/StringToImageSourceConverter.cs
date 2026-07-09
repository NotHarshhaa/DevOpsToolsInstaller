using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevOpsToolsInstaller.Converters;

/// <summary>
/// Converts a logo URL <see cref="string"/> into an <see cref="ImageSource"/>
/// for an <c>Image</c> control. SVG URLs (the Simple Icons CDN we use for brand
/// logos) are loaded via <see cref="SvgImageSource"/>; other formats fall back
/// to <see cref="BitmapImage"/>. An empty/invalid URL returns <c>null</c> so
/// the layered <c>FontIcon</c> glyph shows through as a graceful fallback.
/// </summary>
public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var isSvg = url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Contains("simpleicons", StringComparison.OrdinalIgnoreCase);

        // A failed download/parse simply renders nothing, leaving the glyph
        // beneath the Image visible — so no error handling is required here.
        return isSvg
            ? new SvgImageSource(uri)
            : new BitmapImage(uri);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
