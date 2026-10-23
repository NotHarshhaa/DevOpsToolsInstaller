namespace DevOpsToolsInstaller.Models;

/// <summary>
/// Source backend used to install a tool: direct vendor download (default, zero bloat),
/// or an optional package manager (winget, Scoop, Chocolatey).
/// </summary>
public enum ToolBackend
{
    /// <summary>Direct vendor download from official site (Default, zero package-manager bloat).</summary>
    Direct,

    /// <summary>Windows Package Manager (winget CLI).</summary>
    Winget,

    /// <summary>Scoop command-line installer.</summary>
    Scoop,

    /// <summary>Chocolatey package manager.</summary>
    Chocolatey
}

public static class ToolBackendExtensions
{
    public static string DisplayName(this ToolBackend backend) => backend switch
    {
        ToolBackend.Direct => "Direct Vendor Download (Default)",
        ToolBackend.Winget => "winget (Windows Package Manager)",
        ToolBackend.Scoop => "Scoop",
        ToolBackend.Chocolatey => "Chocolatey",
        _ => "Direct Vendor Download"
    };

    public static string ShortName(this ToolBackend backend) => backend switch
    {
        ToolBackend.Direct => "Direct",
        ToolBackend.Winget => "winget",
        ToolBackend.Scoop => "Scoop",
        ToolBackend.Chocolatey => "Chocolatey",
        _ => "Direct"
    };

    public static ToolBackend Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ToolBackend.Direct;
        return text.Trim().ToLowerInvariant() switch
        {
            "winget" or "msstore" => ToolBackend.Winget,
            "scoop" => ToolBackend.Scoop,
            "choco" or "chocolatey" => ToolBackend.Chocolatey,
            _ => ToolBackend.Direct
        };
    }
}
