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
        var doc = PathDoctorService.Diagnose();

        var valid = doc.CleanedEntries.ToList();
        var missing = doc.MissingDirectories.ToList();

        bool isHealthy = doc.IsHealthy;
        string statusBadge = isHealthy ? "Healthy" : "Needs attention";
        string summary = doc.Summary;

        string suggestedFix = $"[Environment]::SetEnvironmentVariable('PATH', [Environment]::GetEnvironmentVariable('PATH', 'User') + ';{binFolder}', 'User')";

        return new PathHealthReport(
            IsHealthy: isHealthy,
            StatusBadge: statusBadge,
            Summary: summary,
            BinFolder: binFolder,
            IsBinOnUserPath: doc.IsBinOnUserPath,
            IsBinOnProcessPath: Environment.GetEnvironmentVariable("PATH")?.Contains(binFolder, StringComparison.OrdinalIgnoreCase) ?? false,
            SuggestedFixCommand: suggestedFix,
            ValidPathEntries: valid,
            MissingPathEntries: missing);
    }
}
