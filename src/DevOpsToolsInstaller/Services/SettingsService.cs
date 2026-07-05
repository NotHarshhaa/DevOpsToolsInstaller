using System;
using System.IO;
using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

public enum AppTheme
{
    Default,
    Light,
    Dark
}

public static class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "settings.json");

    public static AppTheme Theme { get; set; } = AppTheme.Default;

    public static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var themeProp) &&
                    Enum.TryParse<AppTheme>(themeProp.GetString(), out var theme))
                {
                    Theme = theme;
                }
            }
        }
        catch
        {
            // Fallback to default
        }
    }

    public static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var data = new { Theme = Theme.ToString() };
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }
}
