using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>One catalog tool matched from a devcontainer.json.</summary>
public sealed record DevContainerMatch(string ToolId, string ToolName, string Source, string SourceDetail);

/// <summary>Result of parsing a devcontainer.json and matching it to the catalog.</summary>
public sealed record DevContainerImportResult(
    bool Parsed,
    string? Error,
    string ContainerName,
    IReadOnlyList<DevContainerMatch> Matches,
    IReadOnlyList<string> UnmatchedFeatures,
    IReadOnlyList<string> UnmatchedExtensions)
{
    public static readonly DevContainerImportResult Empty = new(false, "Not parsed.", "", [], [], []);
}

/// <summary>
/// The reverse of <see cref="DevContainerGeneratorService"/>: parses an
/// existing .devcontainer/devcontainer.json and maps its features and VS Code
/// extensions back onto catalog tools so a project's tool requirements can be
/// installed on the workstation in one pass.
/// </summary>
public static class DevContainerImportService
{
    /// <summary>devcontainer feature id → catalog tool id.</summary>
    private static readonly Dictionary<string, string> FeatureToTool = new(StringComparer.OrdinalIgnoreCase)
    {
        // ghcr.io/devcontainers/features/*
        ["kubectl-helm-minikube"] = "kubectl",
        ["docker-outside-of-docker"] = "docker-desktop",
        ["docker-in-docker"] = "docker-desktop",
        ["terraform"] = "terraform",
        ["aws-cli"] = "awscli",
        ["azure-cli"] = "azure-cli",
        ["google-cloud-cli"] = "gcloud-cli",
        ["github-cli"] = "github-cli",
        ["git"] = "git",
        ["git-lfs"] = "git",
        ["powershell"] = "powershell",
        ["python"] = "python",
        ["node"] = "nodejs",
        ["go"] = "golang",
        ["rust"] = "rust",
        ["java"] = "",
        ["docker-from-docker"] = "docker-desktop",
        ["common-utils"] = "",

        // Popular community features (best-effort)
        ["hashicorp-tools"] = "terraform",
        ["k3d"] = "k3d",
        ["kind"] = "kind",
        ["helm"] = "helm",
        ["minikube"] = "minikube",
        ["ansible"] = "ansible",
        ["packer"] = "packer",
        ["vagrant"] = "vagrant",
        ["pulumi"] = "pulumi",
        ["terraform-docs"] = "terraform",
        ["tflint"] = "tflint",
        ["trivy"] = "trivy"
    };

    /// <summary>VS Code extension id → catalog tool id.</summary>
    private static readonly Dictionary<string, string> ExtensionToTool = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hashicorp.terraform"] = "terraform",
        ["ms-kubernetes-tools.vscode-kubernetes-tools"] = "kubectl",
        ["ms-azuretools.vscode-docker"] = "docker-desktop",
        ["ms-vscode.azurecli"] = "azure-cli",
        ["amazonwebservices.aws-toolkit-vscode"] = "awscli",
        ["googlecloudtools.cloudcode"] = "gcloud-cli",
        ["github.vscode-github-actions"] = "github-cli",
        ["github.vscode-pull-request-github"] = "github-cli",
        ["ms-vscode.powershell"] = "powershell",
        ["ms-python.python"] = "python",
        ["golang.go"] = "golang",
        ["rust-lang.rust-analyzer"] = "rust",
        ["vscoss.vscode-ansible"] = "ansible",
        ["ms-vscode.makefile-tools"] = "just",
        ["esbenp.prettier-vscode"] = "nodejs",
        ["dbaeumer.vscode-eslint"] = "nodejs"
    };

    /// <summary>
    /// Parses a devcontainer.json (tolerant of JSONC comments and trailing
    /// commas) and matches its features/extensions against the catalog.
    /// </summary>
    public static DevContainerImportResult ParseFile(string path, IReadOnlyList<ToolDefinition> catalog)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new DevContainerImportResult(false, $"File not found: {path}", "", [], [], []);
            }
            return ParseJson(File.ReadAllText(path), catalog);
        }
        catch (Exception ex)
        {
            return new DevContainerImportResult(false, ex.Message, "", [], [], []);
        }
    }

    public static DevContainerImportResult ParseJson(string json, IReadOnlyList<ToolDefinition> catalog)
    {
        try
        {
            var root = JsonNode.Parse(StripJsonc(json));
            if (root is not JsonObject obj)
            {
                return new DevContainerImportResult(false, "The file does not contain a devcontainer configuration object.", "", [], [], []);
            }

            var name = obj["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(obj["build"]?["dockerfile"]?.GetValue<string>() ?? "devcontainer");

            var toolsById = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            var matches = new List<DevContainerMatch>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unmatchedFeatures = new List<string>();
            var unmatchedExtensions = new List<string>();

            void AddMatch(string toolId, string source, string detail)
            {
                if (string.IsNullOrEmpty(toolId)) return;
                if (!toolsById.TryGetValue(toolId, out var tool)) return;
                if (seen.Add(tool.Id))
                {
                    matches.Add(new DevContainerMatch(tool.Id, tool.Name, source, detail));
                }
            }

            if (obj["features"] is JsonObject features)
            {
                foreach (var (featureId, _) in features)
                {
                    if (string.IsNullOrWhiteSpace(featureId)) continue;

                    // Feature ids look like ghcr.io/org/features/name:1 or ./local —
                    // the feature name is the last path segment, minus the tag.
                    var segment = featureId.TrimStart('.', '/');
                    var lastSegment = segment.Split('/').Last();
                    var featureName = lastSegment.Split(':')[0].Split('@')[0];

                    if (FeatureToTool.TryGetValue(featureName, out var toolId))
                    {
                        AddMatch(toolId, "Feature", featureId);
                    }
                    else if (toolsById.TryGetValue(featureName, out var direct))
                    {
                        // Feature name matches a catalog id directly (e.g. "helm").
                        AddMatch(direct.Id, "Feature", featureId);
                    }
                    else
                    {
                        unmatchedFeatures.Add(featureId);
                    }
                }
            }

            if (obj["customizations"]?["vscode"]?["extensions"] is JsonArray extensions)
            {
                foreach (var ext in extensions)
                {
                    var extId = ext?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(extId)) continue;
                    if (ExtensionToTool.TryGetValue(extId, out var toolId))
                    {
                        AddMatch(toolId, "Extension", extId);
                    }
                    else
                    {
                        unmatchedExtensions.Add(extId);
                    }
                }
            }

            return new DevContainerImportResult(true, null, name, matches, unmatchedFeatures, unmatchedExtensions);
        }
        catch (Exception ex)
        {
            return new DevContainerImportResult(false, $"Could not parse devcontainer.json: {ex.Message}", "", [], [], []);
        }
    }

    /// <summary>
    /// Removes line/block comments and trailing commas so devcontainer.json
    /// files written as JSONC parse with System.Text.Json.
    /// </summary>
    private static string StripJsonc(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        bool inString = false, inLineComment = false, inBlockComment = false;
        bool escape = false;
        char prev = '\0';

        for (int i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (inLineComment)
            {
                if (c == '\n' || c == '\r')
                {
                    inLineComment = false;
                    sb.Append(c);
                }
                prev = c;
                continue;
            }
            if (inBlockComment)
            {
                if (prev == '*' && c == '/')
                {
                    inBlockComment = false;
                    prev = '\0';
                    continue;
                }
                prev = c;
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                prev = c;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                prev = c;
                continue;
            }
            if (prev == '/' && c == '/')
            {
                inLineComment = true;
                // Remove the '/' already appended.
                sb.Length--;
                prev = c;
                continue;
            }
            if (prev == '/' && c == '*')
            {
                inBlockComment = true;
                sb.Length--;
                prev = c;
                continue;
            }
            // Trailing comma: ", }" or ", ]"
            if (c == ',' )
            {
                int j = i + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                {
                    prev = c;
                    continue; // skip the comma
                }
            }

            sb.Append(c);
            prev = c;
        }

        return sb.ToString();
    }
}
