using System.IO.Compression;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Outcome of a post-download artifact action, surfaced to the UI.
/// </summary>
public sealed record ArtifactActionResult(bool Success, string Message);

/// <summary>
/// Performs the appropriate post-download action for a tool based on its
/// <see cref="ArtifactKind"/>:
///   • Installer → launch it (vendor handles its own UAC)
///   • Archive   → extract into Tools\&lt;id&gt; and open the folder
///   • Binary    → copy into Tools\bin (a single PATH-able folder) and open it
///   • Script    → open the containing folder for the user to review
///
/// The app never installs anything silently and never auto-runs a script.
/// </summary>
public static class ArtifactService
{
    /// <summary>
    /// Root folder where extracted archives and standalone binaries live:
    /// %LOCALAPPDATA%\DevOpsToolsInstaller\Tools
    /// </summary>
    public static string ToolsRoot
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevOpsToolsInstaller", "Tools");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>
    /// Single folder for standalone CLI binaries. Users add this to PATH once
    /// and every "Binary" tool becomes available on the command line.
    /// </summary>
    public static string BinFolder
    {
        get
        {
            var folder = Path.Combine(ToolsRoot, "bin");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>
    /// Executes the correct action for the tool's artifact kind.
    /// </summary>
    public static ArtifactActionResult Perform(ToolDefinition tool, string downloadsFolder)
    {
        var sourcePath = Path.Combine(downloadsFolder, tool.FileName);
        if (!File.Exists(sourcePath))
        {
            return new ArtifactActionResult(
                false, $"{tool.FileName} not found — download it first.");
        }

        try
        {
            return tool.Kind switch
            {
                ArtifactKind.Installer => RunInstaller(tool, downloadsFolder),
                ArtifactKind.Archive   => ExtractArchive(tool, sourcePath),
                ArtifactKind.Binary    => InstallBinary(tool, sourcePath),
                ArtifactKind.Script    => RevealScript(sourcePath),
                _                      => RunInstaller(tool, downloadsFolder)
            };
        }
        catch (Exception ex)
        {
            return new ArtifactActionResult(false, $"{tool.Name}: {ex.Message}");
        }
    }

    private static ArtifactActionResult RunInstaller(ToolDefinition tool, string downloadsFolder)
    {
        LauncherService.Launch(tool, downloadsFolder);
        return new ArtifactActionResult(true, $"Launched {tool.Name} installer.");
    }

    private static ArtifactActionResult ExtractArchive(ToolDefinition tool, string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext != ".zip")
        {
            // Only .zip can be extracted in-process; open the file's folder so
            // the user can handle other formats (.tar.gz, .7z) with their tools.
            LauncherService.OpenDownloadsFolder(Path.GetDirectoryName(sourcePath)!);
            return new ArtifactActionResult(
                true, $"{tool.Name}: opened folder ({ext} archives need manual extraction).");
        }

        var target = Path.Combine(ToolsRoot, tool.Id);
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(sourcePath, target, overwriteFiles: true);

        LauncherService.OpenDownloadsFolder(target);
        return new ArtifactActionResult(true, $"{tool.Name} extracted to {target}");
    }

    private static ArtifactActionResult InstallBinary(ToolDefinition tool, string sourcePath)
    {
        var bin = BinFolder;
        var destPath = Path.Combine(bin, tool.FileName);
        File.Copy(sourcePath, destPath, overwrite: true);

        LauncherService.OpenDownloadsFolder(bin);

        var onPath = IsOnUserPath(bin);
        var hint = onPath
            ? "It's on your PATH and ready to use."
            : "Add this folder to your PATH to use it from any terminal.";
        return new ArtifactActionResult(true, $"{tool.Name} copied to Tools\\bin. {hint}");
    }

    private static ArtifactActionResult RevealScript(string sourcePath)
    {
        // Never auto-run remote scripts. Open the folder so the user can review.
        LauncherService.OpenDownloadsFolder(Path.GetDirectoryName(sourcePath)!);
        return new ArtifactActionResult(
            true, "Script downloaded — review it, then run it yourself.");
    }

    /// <summary>
    /// Checks whether <paramref name="folder"/> is present in the current
    /// user's PATH environment variable.
    /// </summary>
    private static bool IsOnUserPath(string folder)
    {
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var processPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var combined = userPath + Path.PathSeparator + processPath;

        return combined
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(
                p.Trim().TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }
}
