using System.Collections.Generic;

namespace DevOpsToolsInstaller.Models;

/// <summary>
/// Represents a curated stack/bundle of tools that can be selected or installed together.
/// </summary>
public sealed class ToolBundle
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<string> Tools { get; set; } = new();
}
