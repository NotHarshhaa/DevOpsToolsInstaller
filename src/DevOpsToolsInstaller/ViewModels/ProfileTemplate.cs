using System.Collections.Generic;

namespace DevOpsToolsInstaller.ViewModels;

/// <summary>
/// A one-click starter profile offered in the Profiles empty state. Wraps a
/// named, curated set of catalog tool ids that becomes a saved
/// <see cref="Services.WorkstationProfile"/> when the user picks it.
/// </summary>
public sealed class ProfileTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE7C3";

    /// <summary>Catalog tool ids that make up the template.</summary>
    public List<string> ToolIds { get; set; } = new();

    /// <summary>How many of the template's tools exist in the loaded catalog.</summary>
    public string ToolCountText { get; set; } = string.Empty;
}