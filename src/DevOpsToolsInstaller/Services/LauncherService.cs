using System.Diagnostics;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class LauncherService
{
    /// <summary>
    /// Launches the downloaded installer using the system shell.
    /// UseShellExecute = true lets the vendor installer show its own UAC prompt.
    /// The calling app does NOT need to be elevated.
    /// </summary>
    public static void Launch(ToolDefinition tool, string downloadsFolder)
    {
        var filePath = Path.Combine(downloadsFolder, tool.FileName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                $"Installer not found: {tool.FileName}. Download it first.", filePath);

        var psi = new ProcessStartInfo
        {
            FileName = filePath,
            Arguments = tool.LaunchArgs ?? "",
            UseShellExecute = true   // triggers vendor UAC if needed
        };

        Process.Start(psi);
    }

    /// <summary>
    /// Opens the downloads folder in Windows Explorer.
    /// </summary>
    public static void OpenDownloadsFolder(string downloadsFolder)
    {
        if (!Directory.Exists(downloadsFolder))
            Directory.CreateDirectory(downloadsFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = downloadsFolder,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
