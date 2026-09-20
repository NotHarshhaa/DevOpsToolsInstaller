using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevOpsToolsInstaller.Services;

public sealed record PathHealthReport(
    bool IsHealthy,
    string StatusBadge,
    string Summary,
    string BinFolder,
    bool IsBinOnUserPath,
    bool IsBinOnProcessPath,
    string SuggestedFixCommand,
    IReadOnlyList<string> ValidPathEntries,
    IReadOnlyList<string> MissingPathEntries);

public interface IPathHealthService
{
    PathHealthReport CheckPathHealth();
}

public sealed class PathHealthService : IPathHealthService
{
    public PathHealthReport CheckPathHealth()
    {
        var binFolder = ArtifactService.BinFolder;
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var processPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var userParts = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var processParts = processPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool isBinOnUserPath = userParts.Any(p => string.Equals(p.TrimEnd('\\'), binFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        bool isBinOnProcessPath = processParts.Any(p => string.Equals(p.TrimEnd('\\'), binFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        var allEntries = userParts.Concat(processParts).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var valid = new List<string>();
        var missing = new List<string>();

        foreach (var entry in allEntries)
        {
            if (Directory.Exists(entry))
            {
                valid.Add(entry);
            }
            else
            {
                missing.Add(entry);
            }
        }

        // Healthy if Tools\bin is present in PATH (or if user hasn't downloaded any standalone binary tools yet, but having bin on PATH is ideal)
        bool isHealthy = isBinOnUserPath || isBinOnProcessPath;

        string statusBadge = isHealthy ? "Healthy" : "Needs attention";
        string summary = isHealthy
            ? "Tools\\bin directory is configured on PATH. CLI tools are ready to execute."
            : "Tools\\bin is not found in your user PATH. Standalone CLIs may require full paths to run.";

        string suggestedFix = $"[Environment]::SetEnvironmentVariable('PATH', [Environment]::GetEnvironmentVariable('PATH', 'User') + ';{binFolder}', 'User')";

        return new PathHealthReport(
            IsHealthy: isHealthy,
            StatusBadge: statusBadge,
            Summary: summary,
            BinFolder: binFolder,
            IsBinOnUserPath: isBinOnUserPath,
            IsBinOnProcessPath: isBinOnProcessPath,
            SuggestedFixCommand: suggestedFix,
            ValidPathEntries: valid,
            MissingPathEntries: missing);
    }
}
