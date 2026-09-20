using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevOpsToolsInstaller.Models;

/// <summary>
/// Represents a curated stack/bundle of tools that can be selected or installed together.
/// </summary>
public sealed class ToolBundle
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category
    {
        get
        {
            if (!string.IsNullOrEmpty(_category)) return _category;
            return Id switch
            {
                "k8s-starter" or "k8s-advanced" or "container-essentials" => "Containerization",
                "cicd-pipeline" => "CI/CD",
                "cloud-engineer" or "aws-developer" => "Cloud",
                "devops-security" => "Security",
                "observability-stack" => "Monitoring",
                "iac-complete" => "IaC",
                "terminal-poweruser" => "Terminal",
                "networking-mesh" => "Networking",
                _ => "General"
            };
        }
        set => _category = value;
    }
    private string _category = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = new();

    public string Glyph => !string.IsNullOrEmpty(Icon) ? Icon : "\uE71D";
    public string ToolsSummary => string.Join(", ", Tools);
}
