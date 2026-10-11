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
    private static readonly HashSet<string> LocalPngLogos = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LocalSvgLogos = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string LogosDir;
    private static readonly bool LogosDirExists;

    static ToolLogoService()
    {
        LogosDir = Path.Combine(AppContext.BaseDirectory, "Assets", "logos");
        try
        {
            LogosDirExists = Directory.Exists(LogosDir);
            if (LogosDirExists)
            {
                foreach (var file in Directory.GetFiles(LogosDir))
                {
                    var name = Path.GetFileName(file);
                    if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        LocalPngLogos.Add(Path.GetFileNameWithoutExtension(name));
                    }
                    else if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        LocalSvgLogos.Add(Path.GetFileNameWithoutExtension(name));
                    }
                }
            }
        }
        catch
        {
            LogosDirExists = false;
        }
    }

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

    private static BitmapImage CreateBitmapImage(Uri uri)
    {
        return new BitmapImage(uri)
        {
            DecodePixelWidth = 48,
            DecodePixelHeight = 48,
            DecodePixelType = DecodePixelType.Logical
        };
    }

    private static ImageSource? ResolveImageSource(string input)
    {
        try
        {
            var cleanId = Path.GetFileNameWithoutExtension(input).ToLowerInvariant();

            // 1. Instant O(1) in-memory check for bundled logos in Assets/logos
            if (LogosDirExists)
            {
                if (LocalPngLogos.Contains(cleanId))
                {
                    var pngPath = Path.Combine(LogosDir, $"{cleanId}.png");
                    return CreateBitmapImage(new Uri(pngPath, UriKind.Absolute));
                }

                if (LocalSvgLogos.Contains(cleanId))
                {
                    var svgPath = Path.Combine(LogosDir, $"{cleanId}.svg");
                    return new SvgImageSource(new Uri(svgPath, UriKind.Absolute));
                }
            }

            // 2. Direct ms-appx:/// URI support
            if (input.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(input);
                return input.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? CreateBitmapImage(uri)
                    : new SvgImageSource(uri);
            }

            // 3. Absolute local file path
            if (Path.IsPathRooted(input) && File.Exists(input))
            {
                var uri = new Uri(Path.GetFullPath(input), UriKind.Absolute);
                return input.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? CreateBitmapImage(uri)
                    : new SvgImageSource(uri);
            }

            // 4. Remote HTTP/HTTPS URL
            if (Uri.TryCreate(input, UriKind.Absolute, out var remoteUri))
            {
                var isSvg = input.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                            || remoteUri.Host.Contains("simpleicons", StringComparison.OrdinalIgnoreCase);

                return isSvg
                    ? new SvgImageSource(remoteUri)
                    : CreateBitmapImage(remoteUri);
            }
        }
        catch
        {
            // Fallback gracefully on parsing/IO error
        }

        return null;
    }
}
