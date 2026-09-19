using System;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DevOpsToolsInstaller.Services;

public static class AppLogoHelper
{
    public static BitmapImage GetLogoImage()
    {
        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            if (File.Exists(logoPath))
            {
                return new BitmapImage(new Uri(logoPath, UriKind.Absolute));
            }
        }
        catch { }

        try
        {
            return new BitmapImage(new Uri("ms-appx:///Assets/app.png"));
        }
        catch { }

        return new BitmapImage();
    }

    public static BitmapImage GetAuthorImage()
    {
        try
        {
            var authorPath = Path.Combine(AppContext.BaseDirectory, "Assets", "author.png");
            if (File.Exists(authorPath))
            {
                return new BitmapImage(new Uri(authorPath));
            }
        }
        catch { }

        return new BitmapImage(new Uri("https://avatars.githubusercontent.com/u/112948305?v=4"));
    }
}
