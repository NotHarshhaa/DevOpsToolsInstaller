using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Provides fast, cached resolution of brand logos for DevOps tools.
/// First looks up local bundled assets in Assets/logos/, then ms-appx:/// URIs,
/// and caches decoded ImageSource instances for peak scrolling performance.
/// </summary>
public static class ToolLogoService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetLogo(string? keyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(keyOrUrl)) return null;

        if (Cache.TryGetValue(keyOrUrl, out var cached))
        {
            return cached;
        }

        var source = ResolveImageSource(keyOrUrl);
        Cache[keyOrUrl] = source;
        return source;
    }

    private static ImageSource? ResolveImageSource(string input)
    {
        try
        {
            var cleanId = Path.GetFileNameWithoutExtension(input).ToLowerInvariant();

            // 1. Direct check in local Assets/logos directory
            var logosDir = Path.Combine(AppContext.BaseDirectory, "Assets", "logos");
            if (Directory.Exists(logosDir))
            {
                var pngPath = Path.Combine(logosDir, $"{cleanId}.png");
                if (File.Exists(pngPath))
                {
                    return new BitmapImage(new Uri(pngPath, UriKind.Absolute));
                }

                var svgPath = Path.Combine(logosDir, $"{cleanId}.svg");
                if (File.Exists(svgPath))
                {
                    return new SvgImageSource(new Uri(svgPath, UriKind.Absolute));
                }
            }

            // 2. Direct ms-appx:/// URI support
            if (input.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(input);
                return input.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? new BitmapImage(uri)
                    : new SvgImageSource(uri);
            }

            // 3. Absolute local file path
            if (File.Exists(input))
            {
                var uri = new Uri(Path.GetFullPath(input), UriKind.Absolute);
                return input.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? new BitmapImage(uri)
                    : new SvgImageSource(uri);
            }

            // 4. Remote HTTP/HTTPS URL
            if (Uri.TryCreate(input, UriKind.Absolute, out var remoteUri))
            {
                var isSvg = input.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                            || remoteUri.Host.Contains("simpleicons", StringComparison.OrdinalIgnoreCase);

                return isSvg
                    ? new SvgImageSource(remoteUri)
                    : new BitmapImage(remoteUri);
            }
        }
        catch
        {
            // Fallback gracefully on parsing/IO error
        }

        return null;
    }
}
