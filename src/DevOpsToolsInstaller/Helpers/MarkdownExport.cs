using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Builds clipboard-friendly Markdown tables from tool selections — handy for
/// READMEs, pull-request checklists and sharing a stack definition.
/// </summary>
public static class MarkdownExport
{
    /// <summary>
    /// Renders tools as a Markdown table: Name | Category | Version | Status.
    /// </summary>
    public static string ToolsTable(IEnumerable<ToolDefinition> tools, string? title = null)
    {
        var list = tools.ToList();
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();
        }

        sb.AppendLine("| Tool | Category | Version | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var tool in list)
        {
            var version = tool.SelectedVersion;
            if (string.IsNullOrWhiteSpace(version)) version = tool.Version;
            if (!string.IsNullOrWhiteSpace(version)) version = $"v{version.TrimStart('v', 'V')}";

            sb.AppendLine(
                $"| {EscapeCell(tool.Name)} | {EscapeCell(tool.Category)} | {EscapeCell(version)} | {EscapeCell(StatusOf(tool))} |");
        }

        return sb.ToString();
    }

    /// <summary>Human status consistent with the app's badges.</summary>
    private static string StatusOf(ToolDefinition tool)
    {
        if (tool.IsInstalled) return "Installed";
        return tool.Status switch
        {
            ToolStatus.Downloaded => "Downloaded",
            ToolStatus.Downloading => "Downloading",
            ToolStatus.Scheduled => "Scheduled",
            ToolStatus.Failed => "Download failed",
            _ => "Not installed"
        };
    }

    /// <summary>Pipe characters would break the table — escape them.</summary>
    private static string EscapeCell(string text) =>
        (text ?? string.Empty).Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
