using System.Diagnostics;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Manages alternative installation backends (winget, Scoop, Chocolatey),
/// keeping Direct Vendor Download as the default (zero package-manager bloat).
/// </summary>
public static class ToolBackendService
{
    private static readonly Lazy<bool> _isWinget = new(() => CheckCommandExists("winget.exe"));
    private static readonly Lazy<bool> _isScoop = new(() => CheckCommandExists("scoop.cmd") || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "scoop.cmd")));
    private static readonly Lazy<bool> _isChoco = new(() => CheckCommandExists("choco.exe") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ChocolateyInstall")));

    public static bool IsWingetAvailable => _isWinget.Value;
    public static bool IsScoopAvailable => _isScoop.Value;
    public static bool IsChocolateyAvailable => _isChoco.Value;

    public static bool IsBackendAvailable(ToolBackend backend) => backend switch
    {
        ToolBackend.Direct => true,
        ToolBackend.Winget => IsWingetAvailable,
        ToolBackend.Scoop => IsScoopAvailable,
        ToolBackend.Chocolatey => IsChocolateyAvailable,
        _ => true
    };

    /// <summary>
    /// Known package mappings for major DevOps tools across package managers.
    /// Used when the tool definition does not supply an explicit "backends" dictionary.
    /// </summary>
    private static readonly Dictionary<string, (string Winget, string Scoop, string Choco)> KnownPackages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["git"] = ("Git.Git", "git", "git"),
            ["github-cli"] = ("GitHub.cli", "gh", "gh"),
            ["kubectl"] = ("Kubernetes.kubectl", "kubectl", "kubernetes-cli"),
            ["helm"] = ("Helm.Helm", "helm", "kubernetes-helm"),
            ["terraform"] = ("Hashicorp.Terraform", "terraform", "terraform"),
            ["packer"] = ("Hashicorp.Packer", "packer", "packer"),
            ["vault"] = ("Hashicorp.Vault", "vault", "vault"),
            ["consul"] = ("Hashicorp.Consul", "consul", "consul"),
            ["nomad"] = ("Hashicorp.Nomad", "nomad", "nomad"),
            ["vagrant"] = ("Hashicorp.Vagrant", "vagrant", "vagrant"),
            ["opentofu"] = ("OpenTofu.tofu", "opentofu", "opentofu"),
            ["docker-desktop"] = ("Docker.DockerDesktop", "docker", "docker-desktop"),
            ["podman-desktop"] = ("RedHat.Podman-Desktop", "podman-desktop", "podman-desktop"),
            ["k9s"] = ("Der推er.k9s", "k9s", "k9s"),
            ["argocd"] = ("Argo.ArgoCD", "argocd", "argocd-cli"),
            ["flux"] = ("FluxCD.Flux", "flux", "flux"),
            ["istioctl"] = ("Istio.istioctl", "istioctl", "istioctl"),
            ["k3d"] = ("Rancher.k3d", "k3d", "k3d"),
            ["kind"] = ("Kubernetes.kind", "kind", "kind"),
            ["minikube"] = ("Kubernetes.minikube", "minikube", "minikube"),
            ["kustomize"] = ("Kubernetes.Kustomize", "kustomize", "kustomize"),
            ["stern"] = ("Stern.Stern", "stern", "stern"),
            ["velero"] = ("VMware.Velero", "velero", "velero"),
            ["awscli"] = ("Amazon.AWSCLI", "aws", "awscli"),
            ["azure-cli"] = ("Microsoft.AzureCLI", "azure-cli", "azure-cli"),
            ["gcloud-cli"] = ("Google.CloudSDK", "gcloud", "gcloudsdk"),
            ["doctl"] = ("DigitalOcean.doctl", "doctl", "doctl"),
            ["vscode"] = ("Microsoft.VisualStudioCode", "vscode", "vscode"),
            ["intellij-idea-community"] = ("JetBrains.IntelliJIDEA.Community", "idea-community", "intellijidea-community"),
            ["pycharm-community"] = ("JetBrains.PyCharm.Community", "pycharm-community", "pycharm-community"),
            ["neovim"] = ("Neovim.Neovim", "neovim", "neovim"),
            ["notepad-plus-plus"] = ("Notepad++.Notepad++", "notepadplusplus", "notepadplusplus"),
            ["windows-terminal"] = ("Microsoft.WindowsTerminal", "windows-terminal", "microsoft-windows-terminal"),
            ["wezterm"] = ("Wez.WezTerm", "wezterm", "wezterm"),
            ["alacritty"] = ("Alacritty.Alacritty", "alacritty", "alacritty"),
            ["dbeaver"] = ("dbeaver.dbeaver", "dbeaver", "dbeaver"),
            ["postman"] = ("Postman.Postman", "postman", "postman"),
            ["insomnia"] = ("Kong.Insomnia", "insomnia", "insomnia"),
            ["jq"] = ("jqlang.jq", "jq", "jq"),
            ["yq"] = ("MikeFarah.yq", "yq", "yq"),
            ["bat"] = ("sharkdp.bat", "bat", "bat"),
            ["eza"] = ("eza-community.eza", "eza", "eza"),
            ["fd"] = ("sharkdp.fd", "fd", "fd"),
            ["ripgrep"] = ("BurntSushi.ripgrep.MSVC", "ripgrep", "ripgrep"),
            ["starship"] = ("Starship.Starship", "starship", "starship"),
            ["zoxide"] = ("ajeetdsouza.zoxide", "zoxide", "zoxide"),
            ["fzf"] = ("junegunn.fzf", "fzf", "fzf"),
            ["lazygit"] = ("JesseDuffield.lazygit", "lazygit", "lazygit"),
            ["lazydocker"] = ("JesseDuffield.lazydocker", "lazydocker", "lazydocker"),
            ["just"] = ("Casey.Just", "just", "just"),
            ["task"] = ("Taskfile.Task", "task", "go-task"),
            ["tflint"] = ("Terraform-Linters.TFLint", "tflint", "tflint"),
            ["terragrunt"] = ("Gruntwork.Terragrunt", "terragrunt", "terragrunt"),
            ["sops"] = ("Getsops.Sops", "sops", "sops"),
            ["trivy"] = ("AquaSecurity.Trivy", "trivy", "trivy"),
            ["gitleaks"] = ("Gitleaks.Gitleaks", "gitleaks", "gitleaks"),
            ["cosign"] = ("Sigstore.Cosign", "cosign", "cosign"),
            ["cloudflared"] = ("Cloudflare.cloudflared", "cloudflared", "cloudflared"),
            ["prometheus"] = ("Prometheus.Prometheus", "prometheus", "prometheus"),
            ["grafana"] = ("GrafanaLabs.Grafana", "grafana", "grafana")
        };

    public static string GetPackageId(ToolDefinition tool, ToolBackend backend)
    {
        if (backend == ToolBackend.Direct) return tool.Id;

        var key = backend.ShortName().ToLowerInvariant();

        // 1. Check tool definition custom dictionary
        if (tool.Backends is not null && tool.Backends.TryGetValue(key, out var customId) && !string.IsNullOrWhiteSpace(customId))
        {
            return customId.Trim();
        }

        // 2. Check built-in curated mapping
        if (KnownPackages.TryGetValue(tool.Id, out var mapping))
        {
            return backend switch
            {
                ToolBackend.Winget => mapping.Winget,
                ToolBackend.Scoop => mapping.Scoop,
                ToolBackend.Chocolatey => mapping.Choco,
                _ => tool.Id
            };
        }

        // 3. Fallback to tool id
        return tool.Id;
    }

    /// <summary>
    /// Executes installation using the selected backend.
    /// </summary>
    public static async Task<ArtifactActionResult> InstallWithBackendAsync(
        ToolDefinition tool,
        ToolBackend backend,
        string downloadsFolder,
        Action<string>? logCallback = null,
        CancellationToken ct = default)
    {
        // Enforce enterprise policy backend allowlist
        if (!EnterprisePolicyService.IsBackendAllowed(backend))
        {
            var msg = $"Backend '{backend.ShortName()}' is blocked by corporate IT policy.";
            ActivityLogService.Error(tool.Name, msg);
            return new ArtifactActionResult(false, msg);
        }

        // Direct vendor download (Default)
        if (backend == ToolBackend.Direct)
        {
            return ArtifactService.Perform(tool, downloadsFolder);
        }

        var pkgId = GetPackageId(tool, backend);
        string exeName;
        string args;

        switch (backend)
        {
            case ToolBackend.Winget:
                if (!IsWingetAvailable) return new ArtifactActionResult(false, "winget is not installed or available on this PC.");
                exeName = "winget.exe";
                args = $"install --id {pkgId} --exact --accept-package-agreements --accept-source-agreements --silent";
                break;

            case ToolBackend.Scoop:
                if (!IsScoopAvailable) return new ArtifactActionResult(false, "Scoop is not installed or available on this PC.");
                exeName = "powershell.exe";
                args = $"-NoProfile -ExecutionPolicy Bypass -Command \"scoop install {pkgId}\"";
                break;

            case ToolBackend.Chocolatey:
                if (!IsChocolateyAvailable) return new ArtifactActionResult(false, "Chocolatey is not installed or available on this PC.");
                exeName = "choco.exe";
                args = $"install {pkgId} -y";
                break;

            default:
                return ArtifactService.Perform(tool, downloadsFolder);
        }

        logCallback?.Invoke($"[{backend.ShortName()}] Installing {tool.Name} ({pkgId})...");
        ActivityLogService.Info(tool.Name, $"Starting install via {backend.ShortName()}: {pkgId}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exeName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    logCallback?.Invoke($"[{backend.ShortName()}] {e.Data}");
                    ActivityLogService.Info(tool.Name, e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    logCallback?.Invoke($"[{backend.ShortName()}] {e.Data}");
                    ActivityLogService.Warn(tool.Name, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0)
            {
                InstallHistoryService.RecordInstall(tool.Id, tool.Name, tool.SelectedVersion, $"Installed via {backend.ShortName()} ({pkgId})");
                ActivityLogService.Info(tool.Name, $"Successfully installed via {backend.ShortName()}.");
                return new ArtifactActionResult(true, $"Successfully installed via {backend.ShortName()} ({pkgId}).");
            }
            else
            {
                var failMsg = $"{backend.ShortName()} exited with code {process.ExitCode}.";
                ActivityLogService.Error(tool.Name, failMsg);
                return new ArtifactActionResult(false, failMsg);
            }
        }
        catch (Exception ex)
        {
            var err = $"Failed to execute {backend.ShortName()}: {ex.Message}";
            ActivityLogService.Error(tool.Name, err);
            return new ArtifactActionResult(false, err);
        }
    }

    private static bool CheckCommandExists(string command)
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paths)
            {
                var full = Path.Combine(p.Trim(), command);
                if (File.Exists(full)) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
