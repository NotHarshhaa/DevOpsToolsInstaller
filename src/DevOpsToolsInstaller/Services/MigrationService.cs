using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed record MigrationResult(
    List<ToolDefinition> MatchedTools,
    List<string> UnmatchedIds,
    string SourceDescription);

/// <summary>
/// Migrates tool lists from other package managers (winget export JSON,
/// chocolatey list output) into DevOpsToolsInstaller catalog tools, via
/// best-effort identifier mapping. Unmatched identifiers are reported so
/// the user knows what the catalog cannot cover.
/// </summary>
public static class MigrationService
{
    /// <summary>
    /// winget package id (e.g. "Git.Git") or choco package name → catalog id.
    /// Best-effort: winget ids drift over time and both managers have far
    /// more packages than this catalog covers — anything unmapped is
    /// reported back to the caller rather than silently dropped.
    /// </summary>
    private static readonly Dictionary<string, string> IdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Version control & Git
        ["Git.Git"] = "git",
        ["git"] = "git",
        ["GitHub.cli"] = "github-cli",
        ["gh"] = "github-cli",
        ["GitLab.CLI"] = "gitlab-cli",
        ["glab"] = "gitlab-cli",
        ["JesseDuffield.Lazygit"] = "lazygit",
        ["lazygit"] = "lazygit",
        ["JesseDuffield.lazydocker"] = "lazydocker",
        ["lazydocker"] = "lazydocker",

        // Kubernetes
        ["Kubernetes.kubectl"] = "kubectl",
        ["kubernetes-cli"] = "kubectl",
        ["Helm.Helm"] = "helm",
        ["helm"] = "helm",
        ["Kubernetes.minikube"] = "minikube",
        ["minikube"] = "minikube",
        ["Kubernetes.kind"] = "kind",
        ["kind"] = "kind",
        ["Derailed.k9s"] = "k9s",
        ["k9s"] = "k9s",
        ["Kubernetes.kustomize"] = "kustomize",
        ["kustomize"] = "kustomize",
        ["kubectx"] = "kubectx",
        ["ahmetb.kubectx"] = "kubectx",
        ["stern"] = "stern",
        ["FluxCD.flux"] = "flux",
        ["flux"] = "flux",
        ["eksctl"] = "eksctl",

        // Containers
        ["Docker.DockerDesktop"] = "docker-desktop",
        ["docker-desktop"] = "docker-desktop",
        ["RedHat.Podman"] = "podman-desktop",
        ["podman"] = "podman-desktop",
        ["RancherDesktop.RancherDesktop"] = "rancher-desktop",
        ["rancher-desktop"] = "rancher-desktop",

        // Cloud CLIs
        ["Amazon.AWSCLI"] = "awscli",
        ["awscli"] = "awscli",
        ["Amazon.SAM-CLI"] = "aws-sam-cli",
        ["aws-sam-cli"] = "aws-sam-cli",
        ["Microsoft.AzureCLI"] = "azure-cli",
        ["azure-cli"] = "azure-cli",
        ["Google.CloudSDK"] = "gcloud-cli",
        ["gcloudsdk"] = "gcloud-cli",
        ["digitalocean.doctl"] = "doctl",
        ["doctl"] = "doctl",
        ["Cloudflare.cloudflared"] = "cloudflared",
        ["cloudflared"] = "cloudflared",
        ["ngrok.ngrok"] = "ngrok",
        ["ngrok"] = "ngrok",

        // IaC & HashiCorp
        ["HashiCorp.Terraform"] = "terraform",
        ["terraform"] = "terraform",
        ["HashiCorp.Vagrant"] = "vagrant",
        ["vagrant"] = "vagrant",
        ["HashiCorp.Packer"] = "packer",
        ["packer"] = "packer",
        ["HashiCorp.Vault"] = "vault",
        ["vault"] = "vault",
        ["HashiCorp.Consul"] = "consul",
        ["consul"] = "consul",
        ["HashiCorp.Nomad"] = "nomad",
        ["nomad"] = "nomad",
        ["OpenTofu.Tofu"] = "opentofu",
        ["opentofu"] = "opentofu",
        ["terragrunt"] = "terragrunt",
        ["tflint"] = "tflint",
        ["infracost"] = "infracost",

        // Observability
        ["GrafanaLabs.GrafanaOSS"] = "grafana",
        ["grafana"] = "grafana",
        ["Prometheus.Prometheus"] = "prometheus",
        ["prometheus"] = "prometheus",
        ["k6.k6"] = "k6",
        ["k6"] = "k6",

        // Security
        ["AquaSecurity.Trivy"] = "trivy",
        ["trivy"] = "trivy",
        ["Anchore.Syft"] = "syft",
        ["syft"] = "syft",
        ["Anchore.Grype"] = "grype",
        ["grype"] = "grype",
        ["Sigstore.Cosign"] = "cosign",
        ["cosign"] = "cosign",
        ["Snyk.Snyk"] = "snyk",
        ["snyk"] = "snyk",
        ["gitleaks"] = "gitleaks",
        ["Mozilla.SOPS"] = "sops",
        ["sops"] = "sops",
        ["OpenPolicyAgent.OPA"] = "opa",
        ["opa"] = "opa",

        // Editors, terminals & utilities
        ["Microsoft.VisualStudioCode"] = "vscode",
        ["vscode"] = "vscode",
        ["Microsoft.WindowsTerminal"] = "windows-terminal",
        ["microsoft-windows-terminal"] = "windows-terminal",
        ["GitLab.gitlab"] = "gitlab-cli",
        ["Notepad++.Notepad++"] = "notepad-plus-plus",
        ["notepadplusplus"] = "notepad-plus-plus",
        ["JetBrains.IntelliJIDEA.Community"] = "intellij-idea-community",
        ["intellijidea-community"] = "intellij-idea-community",
        ["JetBrains.PyCharm.Community"] = "pycharm-community",
        ["pycharm-community"] = "pycharm-community",
        ["Microsoft.VisualStudio.2022.Community"] = "visual-studio-community",
        ["visualstudio2022community"] = "visual-studio-community",
        ["SublimeText.SublimeText4"] = "sublime-text",
        ["SublimeText3"] = "sublime-text",
        ["dbeaver.dbeaver"] = "dbeaver",
        ["dbeaver"] = "dbeaver",
        ["Postman.Postman"] = "postman",
        ["postman"] = "postman",
        ["Insomnia.Insomnia"] = "insomnia",
        ["jqlang.jq"] = "jq",
        ["jq"] = "jq",
        ["mikefarah.yq"] = "yq",
        ["yq"] = "yq",
        ["cURL.cURL"] = "curl",
        ["curl"] = "curl",
        ["junegunn.fzf"] = "fzf",
        ["fzf"] = "fzf",
        ["BurntSushi.ripgrep.MSVC"] = "ripgrep",
        ["ripgrep"] = "ripgrep",
        ["sharkdp.bat"] = "bat",
        ["bat"] = "bat",
        ["sharkdp.fd"] = "fd",
        ["fd"] = "fd",
        ["eza-community.eza"] = "eza",
        ["ajeetdsouza.zoxide"] = "zoxide",
        ["zoxide"] = "zoxide",
        ["Starship.Starship"] = "starship",
        ["starship"] = "starship",
        ["Alacritty.Alacritty"] = "alacritty",
        ["alacritty"] = "alacritty",
        ["wez.wezterm"] = "wezterm",
        ["Neovim.Neovim"] = "neovim",
        ["neovim"] = "neovim",
        ["PuTTY.PuTTY"] = "putty",
        ["putty"] = "putty",
        ["Casey.Just"] = "just",
        ["just"] = "just",
        ["go-task.task"] = "task",
        ["task"] = "task"
    };

    /// <summary>
    /// Parses a winget export file (JSON with Sources[].Packages[].PackageIdentifier).
    /// </summary>
    public static MigrationResult FromWingetExport(string json, IEnumerable<ToolDefinition> catalog)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(json);
        CollectWingetIds(doc.RootElement, ids);

        return MapToCatalog(ids.Distinct(), catalog, "winget export");
    }

    /// <summary>
    /// Parses chocolatey list output — one package per line, either
    /// "name version" or "name|version" (--limit-output format).
    /// </summary>
    public static MigrationResult FromChocoList(string text, IEnumerable<ToolDefinition> catalog)
    {
        var ids = new List<string>();
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Chocolatey", StringComparison.OrdinalIgnoreCase)) continue;

            var name = line.Split('|')[0];
            var parts = name.Split(' ');
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                ids.Add(parts[0]);
            }
        }

        return MapToCatalog(ids.Distinct(), catalog, "chocolatey list");
    }

    private static void CollectWingetIds(JsonElement element, List<string> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var hasId = false;
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name == "PackageIdentifier" && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        ids.Add(prop.Value.GetString()!);
                        hasId = true;
                    }
                }
                if (!hasId)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        CollectWingetIds(prop.Value, ids);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectWingetIds(item, ids);
                }
                break;
        }
    }

    private static MigrationResult MapToCatalog(
        IEnumerable<string> externalIds, IEnumerable<ToolDefinition> catalog, string sourceDescription)
    {
        var byId = catalog.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var matched = new List<ToolDefinition>();
        var unmatched = new List<string>();

        foreach (var externalId in externalIds)
        {
            if (IdMap.TryGetValue(externalId, out var catalogId) && byId.TryGetValue(catalogId, out var tool))
            {
                if (!matched.Contains(tool))
                {
                    matched.Add(tool);
                }
            }
            else
            {
                unmatched.Add(externalId);
            }
        }

        return new MigrationResult(matched, unmatched, sourceDescription);
    }
}
