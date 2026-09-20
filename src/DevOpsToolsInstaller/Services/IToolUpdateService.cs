using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed record ToolUpdateInfo(
    ToolDefinition Tool,
    string Name,
    string CurrentVersion,
    string NewVersion,
    string VersionTransitionText);

public interface IToolUpdateService
{
    Task<IReadOnlyList<ToolUpdateInfo>> CheckForUpdatesAsync(
        IEnumerable<ToolDefinition> installedTools,
        CancellationToken ct = default);
}

public sealed class ToolUpdateService : IToolUpdateService
{
    public async Task<IReadOnlyList<ToolUpdateInfo>> CheckForUpdatesAsync(
        IEnumerable<ToolDefinition> installedTools,
        CancellationToken ct = default)
    {
        var updates = new List<ToolUpdateInfo>();

        foreach (var tool in installedTools)
        {
            ct.ThrowIfCancellationRequested();

            // If detected version isn't populated, attempt to get it from registry or fast probe
            if (string.IsNullOrWhiteSpace(tool.DetectedVersion))
            {
                if (tool.Kind == ArtifactKind.Installer)
                {
                    var regVer = UninstallService.GetInstalledVersion(tool);
                    if (!string.IsNullOrWhiteSpace(regVer))
                    {
                        tool.DetectedVersion = regVer;
                    }
                }
            }

            if (tool.HasUpdate && !string.IsNullOrWhiteSpace(tool.DetectedVersion) && !string.IsNullOrWhiteSpace(tool.Version))
            {
                updates.Add(new ToolUpdateInfo(
                    Tool: tool,
                    Name: tool.Name,
                    CurrentVersion: tool.DetectedVersion,
                    NewVersion: tool.Version,
                    VersionTransitionText: $"{tool.Name} {tool.DetectedVersion} to {tool.Version}"));
            }
        }

        return await Task.FromResult(updates);
    }
}
