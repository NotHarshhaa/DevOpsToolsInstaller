using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Generates production-ready VS Code DevContainer configurations (.devcontainer.json
/// and Dockerfile) tailored to the user's installed tools or curated stacks.
/// </summary>
public static class DevContainerGeneratorService
{
    private static readonly Dictionary<string, string> FeatureMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "kubectl", "ghcr.io/devcontainers/features/kubectl-helm-minikube:1" },
        { "helm", "ghcr.io/devcontainers/features/kubectl-helm-minikube:1" },
        { "minikube", "ghcr.io/devcontainers/features/kubectl-helm-minikube:1" },
        { "docker-cli", "ghcr.io/devcontainers/features/docker-outside-of-docker:1" },
        { "docker-desktop", "ghcr.io/devcontainers/features/docker-outside-of-docker:1" },
        { "terraform", "ghcr.io/devcontainers/features/terraform:1" },
        { "terragrunt", "ghcr.io/devcontainers/features/terraform:1" },
        { "tflint", "ghcr.io/devcontainers/features/terraform:1" },
        { "aws-cli", "ghcr.io/devcontainers/features/aws-cli:1" },
        { "azure-cli", "ghcr.io/devcontainers/features/azure-cli:1" },
        { "google-cloud-cli", "ghcr.io/devcontainers/features/google-cloud-cli:1" },
        { "gh", "ghcr.io/devcontainers/features/github-cli:1" },
        { "git", "ghcr.io/devcontainers/features/git:1" },
        { "powershell", "ghcr.io/devcontainers/features/powershell:1" },
        { "python", "ghcr.io/devcontainers/features/python:3" },
        { "node", "ghcr.io/devcontainers/features/node:1" },
        { "golang", "ghcr.io/devcontainers/features/go:1" },
        { "rust", "ghcr.io/devcontainers/features/rust:1" }
    };

    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "terraform", "hashicorp.terraform" },
        { "terragrunt", "hashicorp.terraform" },
        { "kubectl", "ms-kubernetes-tools.vscode-kubernetes-tools" },
        { "helm", "ms-kubernetes-tools.vscode-kubernetes-tools" },
        { "k9s", "ms-kubernetes-tools.vscode-kubernetes-tools" },
        { "docker-cli", "ms-azuretools.vscode-docker" },
        { "docker-desktop", "ms-azuretools.vscode-docker" },
        { "azure-cli", "ms-vscode.azurecli" },
        { "aws-cli", "amazonwebservices.aws-toolkit-vscode" },
        { "google-cloud-cli", "GoogleCloudTools.cloudcode" },
        { "gh", "github.vscode-github-actions" },
        { "trivy", "trivy.trivy-vscode" },
        { "powershell", "ms-vscode.powershell" },
        { "python", "ms-python.python" },
        { "golang", "golang.go" },
        { "rust", "rust-lang.rust-analyzer" }
    };

    public static string GenerateDevContainerJson(IEnumerable<ToolDefinition> tools, string profileName = "DevOps Workstation")
    {
        var toolList = tools.ToList();
        var selectedIds = new HashSet<string>(toolList.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        var featuresObj = new JsonObject();
        featuresObj["ghcr.io/devcontainers/features/common-utils:2"] = new JsonObject
        {
            ["installZsh"] = true,
            ["configureZshAsDefaultShell"] = true,
            ["upgradePackages"] = true
        };

        var mappedFeatures = new HashSet<string>();
        foreach (var id in selectedIds)
        {
            if (FeatureMap.TryGetValue(id, out var feat) && mappedFeatures.Add(feat))
            {
                featuresObj[feat] = new JsonObject();
            }
        }

        var extensions = new List<string>
        {
            "redhat.vscode-yaml",
            "editorconfig.editorconfig"
        };

        foreach (var id in selectedIds)
        {
            if (ExtensionMap.TryGetValue(id, out var ext) && !extensions.Contains(ext))
            {
                extensions.Add(ext);
            }
        }

        var devContainer = new JsonObject
        {
            ["name"] = $"{profileName} DevContainer",
            ["build"] = new JsonObject
            {
                ["dockerfile"] = "Dockerfile"
            },
            ["features"] = featuresObj,
            ["customizations"] = new JsonObject
            {
                ["vscode"] = new JsonObject
                {
                    ["extensions"] = JsonSerializer.SerializeToNode(extensions),
                    ["settings"] = new JsonObject
                    {
                        ["terminal.integrated.defaultProfile.linux"] = "zsh"
                    }
                }
            },
            ["remoteUser"] = "vscode",
            ["postCreateCommand"] = "echo '🚀 DevOps DevContainer initialized successfully!' && zsh --version"
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        return devContainer.ToJsonString(options);
    }

    public static string GenerateDockerfile(IEnumerable<ToolDefinition> tools)
    {
        var toolList = tools.ToList();
        var selectedIds = new HashSet<string>(toolList.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# DevOpsToolsInstaller - Custom Workstation Dockerfile");
        sb.AppendLine($"# Generated on {DateTime.Now:yyyy-MM-dd HH:mm} with {toolList.Count} configured tool(s)");
        sb.AppendLine("FROM mcr.microsoft.com/devcontainers/base:ubuntu-22.04");
        sb.AppendLine();
        sb.AppendLine("# Install core Linux packages and CLI utilities");
        sb.AppendLine("RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \\");
        sb.AppendLine("    && apt-get install -y --no-install-recommends \\");
        sb.AppendLine("       curl wget ca-certificates git jq unzip xz-utils \\");
        sb.AppendLine("       ripgrep fzf tmux \\");
        sb.AppendLine("    && apt-get clean && rm -rf /var/lib/apt/lists/*");
        sb.AppendLine();

        // Additional standalone binaries for popular tools
        var extraInstalls = new List<string>();

        if (selectedIds.Contains("k9s"))
        {
            extraInstalls.Add("# Install k9s Kubernetes CLI\nRUN curl -sSLo /tmp/k9s.tar.gz https://github.com/derailed/k9s/releases/latest/download/k9s_Linux_amd64.tar.gz \\\n    && tar -xzf /tmp/k9s.tar.gz -C /usr/local/bin k9s \\\n    && chmod +x /usr/local/bin/k9s && rm /tmp/k9s.tar.gz");
        }

        if (selectedIds.Contains("trivy"))
        {
            extraInstalls.Add("# Install Trivy vulnerability scanner\nRUN curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin");
        }

        if (selectedIds.Contains("yq"))
        {
            extraInstalls.Add("# Install yq YAML processor\nRUN curl -sSLo /usr/local/bin/yq https://github.com/mikefarah/yq/releases/latest/download/yq_linux_amd64 \\\n    && chmod +x /usr/local/bin/yq");
        }

        if (selectedIds.Contains("sops"))
        {
            extraInstalls.Add("# Install Mozilla SOPS\nRUN curl -sSLo /usr/local/bin/sops https://github.com/getsops/sops/releases/latest/download/sops-v3.9.0.linux.amd64 \\\n    && chmod +x /usr/local/bin/sops");
        }

        if (selectedIds.Contains("starship"))
        {
            extraInstalls.Add("# Install Starship prompt\nRUN curl -sS https://starship.rs/install.sh | sh -s -- -y");
        }

        if (extraInstalls.Count > 0)
        {
            sb.AppendLine(string.Join("\n\n", extraInstalls));
            sb.AppendLine();
        }

        sb.AppendLine("# Switch to non-root user");
        sb.AppendLine("USER vscode");
        sb.AppendLine("WORKDIR /home/vscode");

        return sb.ToString();
    }

    public static string GenerateReadme(IEnumerable<ToolDefinition> tools, string profileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {profileName} DevContainer");
        sb.AppendLine();
        sb.AppendLine("This DevContainer setup was generated by **DevOpsToolsInstaller**.");
        sb.AppendLine("It reproduces your Windows DevOps workstation toolchain inside an isolated Linux container.");
        sb.AppendLine();
        sb.AppendLine("## Included Tools");
        foreach (var t in tools.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            sb.AppendLine($"- **{t.Name}** ({t.Category}) - {t.Description}");
        }
        sb.AppendLine();
        sb.AppendLine("## Getting Started");
        sb.AppendLine("1. Place the `.devcontainer` folder in the root of your project repository.");
        sb.AppendLine("2. Open the project folder in **Visual Studio Code**.");
        sb.AppendLine("3. When prompted, click **Reopen in Container** (or press `F1` and select `Dev Containers: Reopen in Container`).");
        sb.AppendLine("4. VS Code will build the container with all specified DevOps features and extensions ready to use.");
        return sb.ToString();
    }

    public static void ExportDevContainerFolder(IEnumerable<ToolDefinition> tools, string destinationFolder, string profileName = "DevOps Workstation")
    {
        var devContainerDir = Path.Combine(destinationFolder, ".devcontainer");
        Directory.CreateDirectory(devContainerDir);

        var jsonContent = GenerateDevContainerJson(tools, profileName);
        var dockerfileContent = GenerateDockerfile(tools);
        var readmeContent = GenerateReadme(tools, profileName);

        File.WriteAllText(Path.Combine(devContainerDir, "devcontainer.json"), jsonContent);
        File.WriteAllText(Path.Combine(devContainerDir, "Dockerfile"), dockerfileContent);
        File.WriteAllText(Path.Combine(devContainerDir, "README.md"), readmeContent);
    }

    public static void ExportDevContainerZip(IEnumerable<ToolDefinition> tools, string zipFilePath, string profileName = "DevOps Workstation")
    {
        var jsonContent = GenerateDevContainerJson(tools, profileName);
        var dockerfileContent = GenerateDockerfile(tools);
        var readmeContent = GenerateReadme(tools, profileName);

        if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

        using var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

        var jsonEntry = archive.CreateEntry(".devcontainer/devcontainer.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(jsonEntry.Open(), Encoding.UTF8))
        {
            writer.Write(jsonContent);
        }

        var dockerEntry = archive.CreateEntry(".devcontainer/Dockerfile", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(dockerEntry.Open(), Encoding.UTF8))
        {
            writer.Write(dockerfileContent);
        }

        var readmeEntry = archive.CreateEntry(".devcontainer/README.md", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
        {
            writer.Write(readmeContent);
        }
    }
}
