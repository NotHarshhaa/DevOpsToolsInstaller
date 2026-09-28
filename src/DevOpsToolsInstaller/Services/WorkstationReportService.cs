using System.Text;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Builds a point-in-time markdown inventory of the workstation: which
/// catalog tools are installed, their detected versions, whether updates are
/// available, and the app's security-relevant settings. Intended for audits,
/// support tickets, and machine handovers.
/// </summary>
public static class WorkstationReportService
{
    public static string DefaultReportPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"DevOpsToolsInstaller-report-{DateTime.Now:yyyyMMdd-HHmm}.md");

    /// <summary>
    /// Builds the markdown report and writes it to <paramref name="path"/>.
    /// Returns the number of installed tools included.
    /// </summary>
    public static async Task<int> WriteReportAsync(
        IEnumerable<ToolDefinition> tools, string path, CancellationToken ct = default)
    {
        var (markdown, installedCount) = await BuildMarkdownAsync(tools, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false), ct);
        return installedCount;
    }

    public static async Task<(string Markdown, int InstalledCount)> BuildMarkdownAsync(
        IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var installed = new List<ToolDefinition>();

        await Task.Run(() =>
        {
            foreach (var tool in tools)
            {
                if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
                {
                    tool.IsInstalled = true;
                    if (tool.Kind == ArtifactKind.Installer && string.IsNullOrWhiteSpace(tool.DetectedVersion))
                    {
                        var v = UninstallService.GetInstalledVersion(tool);
                        if (!string.IsNullOrWhiteSpace(v)) tool.DetectedVersion = v;
                    }
                    installed.Add(tool);
                }
            }
        }, ct);

        // Cheap update detection: registry/probed versions vs catalog, without
        // launching any processes (keep report generation fast).
        var updates = await new ToolUpdateService().CheckForUpdatesAsync(installed, ct);
        var updateById = updates.ToDictionary(u => u.Tool.Id, u => u, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# DevOps Tools Installer — Workstation Report");
        sb.AppendLine();
        sb.AppendLine($"- **Machine:** {Environment.MachineName}");
        sb.AppendLine($"- **User:** {Environment.UserName}");
        sb.AppendLine($"- **Generated:** {DateTime.Now:yyyy-MM-dd HH:mm} ({TimeZoneInfo.Local.DisplayName})");
        sb.AppendLine($"- **Installer version:** v{AppUpdaterService.CurrentVersion}");
        sb.AppendLine($"- **Catalog tools:** {tools.Count()}");
        sb.AppendLine($"- **Installed tools:** {installed.Count}");
        sb.AppendLine($"- **Updates available:** {updates.Count}");
        sb.AppendLine();
        sb.AppendLine("## Security-relevant settings");
        sb.AppendLine();
        sb.AppendLine($"| Setting | Value |");
        sb.AppendLine($"| :--- | :--- |");
        sb.AppendLine($"| Signature policy | {SettingsService.SignaturePolicy} |");
        sb.AppendLine($"| Custom proxy | {(SettingsService.ProxyEnabled ? string.IsNullOrWhiteSpace(SettingsService.ProxyUrl) ? "enabled" : $"enabled ({SettingsService.ProxyUrl})" : "disabled (system proxy)")} |");
        sb.AppendLine($"| Downloads folder | `{DownloadService.DefaultDownloadsFolder}` |");
        sb.AppendLine($"| Tools\\bin on PATH | {(SettingsService.IsFolderOnUserPath(ArtifactService.BinFolder) ? "yes" : "no")} |");
        sb.AppendLine();
        sb.AppendLine("## Installed tools");
        sb.AppendLine();
        sb.AppendLine("| Tool | ID | Category | Detected version | Catalog version | Update |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var tool in installed.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            var updateCell = updateById.TryGetValue(tool.Id, out var upd)
                ? $"→ {upd.NewVersion}"
                : "up to date";
            var detected = string.IsNullOrWhiteSpace(tool.DetectedVersion) ? "—" : tool.DetectedVersion;
            sb.AppendLine($"| {tool.Name} | `{tool.Id}` | {tool.Category} | {detected} | {tool.Version} | {updateCell} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Tools with pending updates");
        sb.AppendLine();
        if (updates.Count == 0)
        {
            sb.AppendLine("_None — every installed tool matches the catalog._");
        }
        else
        {
            foreach (var upd in updates)
            {
                sb.AppendLine($"- **{upd.Name}**: {upd.CurrentVersion} → {upd.NewVersion}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("_Generated by DevOps Tools Installer — no telemetry, this file never leaves the machine._");

        return (sb.ToString(), installed.Count);
    }
}
