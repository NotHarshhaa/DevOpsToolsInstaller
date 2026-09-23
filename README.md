# DevOpsToolsInstaller

<p align="center">
  <em>Provision a complete DevOps workstation on Windows in minutes — 90 official tools, curated stacks,<br/>resumable downloads, headless automation, and a security-first pipeline. Zero silent installs, zero bundled binaries, zero telemetry.</em>
</p>

<p align="center">
  <img alt="Release" src="https://img.shields.io/badge/release-v2.5.0-blue?logo=github" />
  <img alt="Build" src="https://github.com/NotHarshhaa/DevOpsToolsInstaller/actions/workflows/release.yml/badge.svg" />
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" />
  <img alt="UI" src="https://img.shields.io/badge/UI-WinUI%203%20%7C%20Mica%20Alt-2b579a" />
  <img alt="CLI" src="https://img.shields.io/badge/headless-CLI%20ready-1F6FEB?logo=windowsterminal" />
  <img alt="Security" src="https://img.shields.io/badge/security-signed%20catalog%20%7C%20HTTPS--only%20%7C%20MOTW-107C41?logo=shield" />
  <img alt="Arch" src="https://img.shields.io/badge/arch-x64%20%7C%20arm64-lightgrey" />
  <img alt="License" src="https://img.shields.io/badge/license-Apache--2.0-green" />
</p>

<p align="center">
  <img src="assets/banner.png" alt="DevOps Tools Installer Showcase Banner" width="100%" />
</p>

A high-performance, native Windows 11 desktop application (WinUI 3 + Mica Alt) designed to provision cloud, container, Kubernetes, IaC, security, database, and terminal tools on a fresh workstation in minutes — with the visual polish and the security posture of a first-party Windows app.

> [!NOTE]  
> DevOpsToolsInstaller does **not** install anything silently. It downloads official vendor artifacts directly from upstream release endpoints with real-time progress and **resumable transfers**, validates Authenticode digital signatures and SHA-256 hashes, tags downloads with Mark-of-the-Web so SmartScreen stays active, and triggers the appropriate context-aware action — launching the vendor's setup wizard, unpacking an archive, or placing a standalone CLI into an isolated user tools folder.

---

## ✨ Highlights

| | |
| :--- | :--- |
| 🪟 **Truly native Windows 11** | Mica Alt backdrop, Fluent `CommandBar` toolbars, your **own system accent color**, `InfoBadge` update counters, Segoe Fluent Icons — no custom theme hacks. |
| 📦 **90 official tools, 12 curated stacks** | Direct-from-vendor artifacts with role-based presets (Kubernetes, DevSecOps, Cloud, IaC, Observability, Terminal). |
| ⏯️ **Resumable download engine** | Interrupted transfers (dropped Wi-Fi, cancelled batches) continue via HTTP `Range` from a `.partial` file — large installers like Docker Desktop never restart from zero. |
| 🛡️ **Security-first pipeline** | HTTPS-only fetches → SHA-256 verification → Authenticode `WinVerifyTrust` with a configurable **Warn / Block** policy → Mark-of-the-Web tagging → on-disk audit log → **cryptographically signed catalog** (fail-closed). |
| 🖥️ **Headless CLI mode** | `DevOpsToolsInstaller.exe --install kubectl,terraform` for scripts, machine provisioning, and CI — with proper exit codes. |
| 📌 **System tray + update scheduler** | Close-to-tray with quick actions, background tool-update scans every 30 minutes, toast notifications with a Settings toggle. |
| 🔍 **Zero trust, zero telemetry** | No analytics, no phone-home, no background services. Everything it does is logged locally and inspectable. |

---

## Contents

- [Why](#why)
- [Who This Is For](#who-this-is-for)
- [How It Works](#how-it-works)
- [Application Tour & Navigation](#application-tour--navigation)
- [Headless CLI Mode](#headless-cli-mode)
- [Tool Categories (90 Tools)](#tool-categories-90-tools)
- [Curated Stacks & Workstation Presets](#curated-stacks--workstation-presets)
- [What Happens After Download](#what-happens-after-download)
- [Resumable Download Engine](#resumable-download-engine)
- [Installed Tools, Health Checks & Shell Completions](#installed-tools-health-checks--shell-completions)
- [Adding `Tools\bin` to Your PATH](#adding-toolsbin-to-your-path)
- [Security, Integrity & Privacy](#security-integrity--privacy)
- [In-App Auto-Updater & Tray](#in-app-auto-updater--tray)
- [Installation & Deployment Options](#installation--deployment-options)
- [Tech Stack](#tech-stack)
- [Catalog Format, Signing & Custom Tools](#catalog-format-signing--custom-tools)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Contributing](#contributing)
- [License](#license)

---

## Why

Setting up a new DevOps workstation usually means the same tedious routine: open a dozen browser tabs, chase down vendor download URLs, verify versions, extract archives, configure PATH variables, and run each setup by hand.

**DevOpsToolsInstaller** unifies this workflow into a single, beautifully designed workstation control plane:
- Browse an extensible catalog of **90 official developer and DevOps tools**.
- Choose role-based **Curated Stacks** for rapid 1-click provisioning or catalog filtering.
- Select previous release versions directly from tool cards.
- Install from a **script or terminal** with headless CLI commands and CI-friendly exit codes.
- Trust every byte: SHA-256 checksums, Authenticode signatures, HTTPS-only fetches, and a signed catalog.
- Enjoy a translucent **Windows 11 Mica Alt** interface that follows **your** accent color, with zero background bloat, zero telemetry, and zero hidden installs.

---

## Who This Is For

- **DevOps, Platform & SRE Engineers** setting up or reprovisioning Windows laptops.
- **Cloud Architects & Developers** working across AWS, Azure, GCP, and Kubernetes ecosystems.
- **IT & Security Teams** who need transparent vendor downloads, signature auditing, a configurable unsigned-installer policy, audit logs, and zero system tampering.
- **Automation & Provisioning Engineers** who want the same toolchain installs reproducible from a script.

---

## How It Works

```
┌─────────────────────────┐   Signed Catalog (ECDSA P-256)   ┌────────────────────────┐
│  DevOps Tools Catalog   │  ──────── verified ───────────►  │   Official Artifact    │
│  (90 Tools / 12 Stacks) │        HTTPS-only fetch          │  (.msi, .exe, .zip)   │
└─────────────────────────┘                                  └───────────┬────────────┘
                                                                         │
                                                Cryptographic Checks     ▼
                                  ┌──────────────────────────────────────────────────────┐
                                  │ SHA-256 Hash + Authenticode WinVerifyTrust + MOTW    │
                                  │ (signature policy: Warn before launch / Block)       │
                                  └───────────────────────────────┬──────────────────────┘
                                                                  │
                                  ┌───────────────────────────────┴──────────────────────┐
                                  ▼                                                      ▼
                      Installer (.msi / .exe)                        Archive / CLI Binary
                      Standard Vendor Wizard                        %LOCALAPPDATA%\...\Tools\bin
                      (User UAC Prompt)                             (1-Click User PATH Setup)
```

1. **Select Tools or Stacks** — in the UI, or from a terminal with `--install` / `--install-bundle`.
2. **Direct Official Download** — fetched straight from official vendor repositories (GitHub Releases, AWS, Azure, HashiCorp, CNCF) over HTTPS only, with automatic resume of interrupted transfers.
3. **Integrity Validation** — SHA-256 hash verification, Win32 `WinVerifyTrust` signature checks, configurable unsigned-installer policy, and Mark-of-the-Web tagging so Windows SmartScreen stays in the loop.
4. **Context-Aware Deployment** — launch official installers, extract archives, or deploy CLI binaries directly to your workstation.

---

## Application Tour & Navigation

The application uses the Windows 11 **Mica Alt** translucent canvas, **your system accent color** for all highlights and actions, native **Segoe Fluent Icons**, and a clean layered layout. Page state (search text, filters, selections, scroll positions) is preserved as you navigate — nothing resets when you hop between sections.

- **🏠 Home (`HomePage`)**: Workstation dashboard with live metrics (installed tools, updates available, active downloads, PATH health), an **Active Downloads** strip with per-tool progress, curated stack cards with install progress, an **Updates Available** section with one-click "Update all", and a Popular & Essential tools list with 1-click install.
- **📦 Tool Catalog (`CatalogPage`)**:
  - **90 Cataloged Tools**: Multi-category browsing with official vector SVG logos.
  - **Native `CommandBar` toolbar**: Sort, downloaded-only filter, presets, select/clear, profile import/export — with Fluent hover states and automatic overflow.
  - **Multi-Version Selector**: Switch between the latest version or specific previous releases with dynamic URL resolution.
  - **Category Filter Chips + Favorites (★)**: Instant filtering; star preferred tools.
  - **Profile Import & Export**: Export your tool selections to a `.json` profile file to share across teams or restore setups instantly.
  - **Active Stack Banner**: Interactive banner showing current stack filtering with a single-click "Clear Filter" action.
- **🚀 Curated Stacks (`StacksPage`)**: 12 role-based workstation bundles with live installed-count badges, tool logo previews, **Install Stack** (batch download + navigate to live progress) and **Customize in Catalog** actions.
- **📥 Downloads (`DownloadsPage`)**: Concurrent download manager (up to 3 parallel transfers) with live speeds, percentages, Authenticode signature badges, status filter chips, an in-app **Activity & Diagnostic Log**, and post-download action triggers.
- **🩺 Installed Tools (`InstalledPage`)**:
  - Complete inventory with a native **InfoBadge update counter** on the navigation item.
  - **CLI Health Probing**: Executes binaries (`--version` / `-v`) to measure latency (ms) and verify runtime health.
  - **One-click tool updates** that download **and** run the vendor installer.
  - **Shell Autocompletion Generator**: Generates autocompletion scripts for PowerShell and Bash with 1-click insertion into `$PROFILE`.
  - **Safe Uninstallation**: Invokes vendor uninstallers via Windows Registry detection or cleanly removes extracted files.
- **⚙️ Settings (`SettingsPage`)**:
  - Theme customization (Light, Dark, or System Default) using **your Windows accent color**.
  - **Security section**: installer signature policy (Warn / Block unsigned), always-on protections summary, and one-click access to the audit log folder.
  - **Toast Notifications & Close-to-Tray toggles**.
  - One-click **"Add to PATH"** with real-time PATH inspection.
  - Disk cache monitor and cleanup; desktop and Start Menu shortcut creation.
  - Manual **"Check for Updates"** triggering the GitHub update service.
- **📌 System Tray**: Close the window to keep the app running in the tray (toggle in Settings). Left-click restores the window; right-click offers **Open**, **Open Tool Catalog**, **Check for tool updates**, and **Exit**.
- **🔔 Toast Notifications**: Background installs, batch downloads, and update checks surface Windows toasts (toggle in Settings; delivery is best-effort and never interrupts the workflow).
- **ℹ️ About (`AboutPage`)**: Architecture specifications, workstation diagnostics, complete **Security Risks & Precautions**, and legal trademark disclaimers.
- **🔍 Global AutoSuggestBox**: Embedded directly in the left navigation pane for real-time catalog search and direct query forwarding from anywhere in the app.

---

## Headless CLI Mode

Everything the UI does is scriptable. Point it at a fresh machine, a rebuild script, or a CI job — the process attaches to your terminal, prints progress, and exits with meaningful codes.

```text
PS> DevOpsToolsInstaller.exe --list
DevOps Tools Installer v2.5.0 — 90 tools available

  act                      act                          CI/CD and Version Control
  dagger                   Dagger                       CI/CD and Version Control
  git                      Git for Windows              CI/CD and Version Control
  ...

PS> DevOpsToolsInstaller.exe --install kubectl,terraform,helm
[get ] kubectl (kubectl.exe)
       kubectl: 74%
[ok  ] kubectl: copied to Tools\bin. It's on your PATH and ready to use.
...

PS> DevOpsToolsInstaller.exe --install-bundle k8s-starter
Done. 5 installed, 0 already present, 0 failed.
```

| Command | Description |
| :--- | :--- |
| `--list` | List every tool in the catalog (id, name, category). |
| `--status` | Show which catalog tools are installed, with detected versions. |
| `--install <id,id,...>` | Download and install specific tools (skips already-installed). |
| `--install-bundle <bundleId>` | Install every tool in a curated stack. |
| `--help` | Show usage. |

- **Exit codes**: `0` = success, `1` = one or more tools failed — perfect for provisioning scripts.
- **Already installed** tools are detected and skipped automatically.
- Vendor installers still run interactively (their own wizard + UAC) exactly like in the UI — nothing bypasses your security policy in headless mode.

---

## Tool Categories (90 Tools)

The catalog organizes 90 essential tools across 12 distinct domains:

1. **Cloud Provider CLIs**: AWS CLI, Azure CLI, Google Cloud CLI, OCI CLI, AWS SAM CLI, eksctl, Azure Functions Core Tools.
2. **Containerization & Runtimes**: Docker Desktop, Podman Desktop, Lazydocker, Dive, Kind, Minikube.
3. **Kubernetes Tooling**: kubectl, Helm, k9s, Stern, Kustomize, kubectx, kubens, Helmfile, Cilium CLI, Linkerd, Istioctl, Velero.
4. **Infrastructure as Code (IaC)**: Terraform, OpenTofu, Pulumi, Terragrunt, TFLint, Packer, Ansible, Infracost.
5. **CI/CD & Version Control**: Git, GitHub CLI, GitLab CLI, ArgoCD CLI, Flux CLI, Tekton CLI (`tkn`), Act (local GitHub Actions runner), Dagger, Task.
6. **Security & Secrets Management**: HashiCorp Vault, SOPS, Gitleaks, Snyk CLI, Kyverno CLI.
7. **Policy, Governance & Compliance**: Trivy, Checkov, tfsec, Syft, Grype, Cosign, Open Policy Agent (`opa`).
8. **Networking & Tunneling**: ngrok, Cloudflare Tunnel (`cloudflared`), Tailscale, Wireshark, Nmap, ctop.
9. **Database & Data DevOps**: Flyway CLI, Liquibase, pgcli, mycli, usql, Redis CLI.
10. **Monitoring & Observability**: Prometheus, Grafana, k6 (load testing), Vector, LogCLI.
11. **Developer Editors & Terminals**: Visual Studio Code, Windows Terminal, PyCharm Community, Cursor, Neovim.
12. **Core Utilities & Performance**: jq, yq, Postman, curl, HTTPie, Starship, fzf, ripgrep, bat, fd, eza, zoxide, Delta, PuTTY, WinSCP, 7-Zip.

*(Full specifications live in [`catalog/catalog.json`](catalog/catalog.json).)*

---

## Curated Stacks & Workstation Presets

The **Curated Stacks** section offers 12 opinionated, battle-tested bundles designed to provision a machine for specific engineering disciplines:

| Stack | Description | Core Tools Included |
| :--- | :--- | :--- |
| **Kubernetes Starter Pack** | Core toolset for local and remote cluster management | `kubectl`, `helm`, `k9s`, `minikube`, `stern`, `kustomize`, `kubectx` |
| **Kubernetes Advanced** | Production GitOps, service mesh, and disaster recovery | `kubectl`, `helm`, `argocd`, `flux`, `istioctl`, `cilium-cli`, `velero`, `helmfile`, `k9s` |
| **Cloud Engineer Essentials** | Multi-cloud infrastructure and policy provisioning | `awscli`, `azure-cli`, `gcloud-cli`, `terraform`, `terragrunt`, `tflint`, `infracost`, `vault` |
| **AWS Developer Kit** | Toolkit for AWS cloud, containers, and serverless | `awscli`, `aws-sam-cli`, `eksctl`, `terraform`, `docker-desktop` |
| **DevSecOps Toolkit** | Vulnerability scanning, container signing, and policy auditing | `trivy`, `gitleaks`, `sops`, `cosign`, `syft`, `grype`, `opa`, `kyverno-cli` |
| **CI/CD Pipeline Builder** | Local runner automation and pipeline construction | `git`, `github-cli`, `act`, `dagger`, `task`, `docker-desktop`, `tkn` |
| **IaC Complete** | Comprehensive infrastructure-as-code suite | `terraform`, `opentofu`, `pulumi`, `terragrunt`, `tflint`, `packer`, `vault`, `infracost` |
| **Observability Stack** | Metrics, distributed logging, and performance benchmarking | `prometheus`, `grafana`, `k6`, `vector`, `logcli` |
| **Terminal Power User** | Blazing-fast modern CLI productivity enhancements | `windows-terminal`, `starship`, `fzf`, `ripgrep`, `bat`, `fd`, `eza`, `zoxide`, `delta`, `jq`, `yq` |
| **Container Essentials** | Container build, debug, and vulnerability inspection | `docker-desktop`, `lazydocker`, `dive`, `trivy`, `cosign`, `syft` |
| **Developer Workstation** | Complete one-click bootstrap for a fresh developer machine | `git`, `github-cli`, `vscode`, `docker-desktop`, `kubectl`, `helm`, `terraform`, `jq`, `postman`, `windows-terminal`, `starship` |
| **Networking & Service Mesh** | Secure ingress tunneling and service-to-service networking | `ngrok`, `cloudflared`, `linkerd`, `istioctl`, `cilium-cli` |

### Dual-Action Workflow
- **Install Stack**: Queues and batch-downloads every tool in the stack (skipping anything already installed) and jumps you to live progress.
- **Select Stack**: Applies a live filter to the Catalog, enabling you to review, customize, or selectively install tools in the stack.

> [!TIP]
> Use **"Select Stack"** to preview tools in the catalog and customize your installation with selective checkboxes before queueing downloads.

---

## What Happens After Download

Every tool in the catalog is assigned a **kind**, dictating the post-download action:

| Kind | Artifact Formats | Action | Button |
| :--- | :--- | :--- | :--- |
| **Installer** | `.msi`, `.exe` | Launches official vendor setup wizard (surfaces standard Windows UAC prompt) after signature-policy evaluation | `Install` |
| **Archive** | `.zip` | Extracts cleanly into per-tool sandbox (`Tools\<tool-id>`) and copies executables into `Tools\bin` | `Extract` |
| **Binary** | `.exe` | Places the standalone binary directly into `Tools\bin` | `Add to Tools` |
| **Script** | `.ps1`, `.sh` | Opens folder for manual inspection — **scripts are never executed automatically** | `Open Folder` |

> [!CAUTION]
> Vendor setup scripts (`.ps1`, `.sh`) are never executed automatically by the app. Always inspect script contents before running them manually in an elevated PowerShell session.

### Where Files Are Stored
- **Downloaded Artifacts**: `%LOCALAPPDATA%\DevOpsToolsInstaller\Downloads`
- **Extracted Archives**: `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\<tool-id>`
- **Portable CLI Binaries**: `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin`
- **Audit Logs**: `%LOCALAPPDATA%\DevOpsToolsInstaller\logs`

---

## Resumable Download Engine

Large vendor artifacts (Docker Desktop is ~550 MB) shouldn't restart from zero because a hotel Wi-Fi hiccuped:

- Every transfer is written to a `<file>.partial` companion.
- On retry — same session or after an app restart — the app sends an HTTP `Range` request and **continues from the exact byte offset** (logged in the Activity Log as *"Resuming download from X MB"*). If the server doesn't support ranges, it restarts cleanly.
- The partial file is promoted to its final name **only after SHA-256 verification passes**; a corrupt partial is discarded rather than resumed.
- Cancelling or losing connection **keeps** your progress.

---

## Installed Tools, Health Checks & Shell Completions

Navigate to the **Installed** tab to manage and verify your local workstation environment:

### 🩺 Real-Time CLI Health Checks
- Directly invokes installed tool binaries using their standard version flags (`--version`, `-v`, or `version`).
- Measures process execution latency in milliseconds.
- Click **"Check All CLIs"** to execute batch diagnostics across your entire toolset.

### ⚡ Shell Autocompletion Generator
- Generates native completion scripts for **PowerShell** and **Bash** for tools like `kubectl`, `helm`, `gh`, `docker`, `terraform`, `podman`, and more.
- Provides a one-click **"Add to PowerShell Profile"** button that automatically appends the completion snippet to `$PROFILE` without manual editing.

---

## Adding `Tools\bin` to Your PATH

Standalone CLI binaries (`kubectl`, `kind`, `jq`, `yq`, `helm`, etc.) land in `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin`. 

> [!TIP]
> Open **Settings** in the app and click **"Add to PATH"**. The app immediately registers the folder in your User Environment (`HKCU\Environment\PATH`) with zero administrator elevation required.

Alternatively, to add it via PowerShell:
```powershell
$bin  = "$env:LOCALAPPDATA\DevOpsToolsInstaller\Tools\bin"
$user = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($user -notlike "*$bin*") {
    [Environment]::SetEnvironmentVariable("PATH", "$user;$bin", "User")
    Write-Host "Added $bin to your user PATH. Restart your terminal to apply."
}
```

> [!IMPORTANT]  
> After updating your PATH (either via the in-app Settings button or PowerShell), restart any active terminal instances (PowerShell, CMD, Windows Terminal, or VS Code) so they can detect the newly added CLI binaries.

---

## Security, Integrity & Privacy

DevOpsToolsInstaller was built with an uncompromising, layered security model:

1. **Signed Catalog (Fail-Closed)**: The tool catalog and stack definitions are **cryptographically signed with an ECDSA P-256 key** at release time and verified against a public key pinned inside the app. Unsigned, expired, or tampered remote catalogs are refused outright — the app falls back to the embedded catalog baked in at build time. A compromised mirror or repository cannot redirect your downloads.
2. **HTTPS-Only Downloads**: Every artifact URL is validated before fetching. Plain-text HTTP fetches are refused and logged — an on-path attacker cannot swap binaries mid-transfer.
3. **SHA-256 Hash Validation**: Catalog entries include cryptographic checksums verified against every download. Hash mismatches delete the payload automatically. Downloads are written to `.partial` files and promoted only after verification.
4. **Win32 Authenticode Verification with Configurable Policy**: Native `WinVerifyTrust` inspects digital signatures on downloaded `.exe`/`.msi` installers before launch. Choose your strictness in **Settings → Security**:
   - **Warn before launch** (default) — confirm before launching unsigned/untrusted installers.
   - **Block unsigned installers** — refuse to launch them entirely. The policy is enforced everywhere: UI, batch installs, and headless CLI mode.
5. **Mark-of-the-Web (MOTW)**: Every downloaded file is tagged with the Windows `Zone.Identifier` stream (ZoneId=3), so **SmartScreen and Microsoft Defender evaluate it exactly like a browser download** — the app never creates a blind spot in your OS defenses.
6. **On-Disk Security Audit Log**: Every download, signature verdict, policy block, install, and uninstall is appended to a daily audit file at `%LOCALAPPDATA%\DevOpsToolsInstaller\logs\activity-YYYYMMDD.log` — a complete, inspectable trail for post-incident review and compliance.
7. **No Silent Installs**: The app never executes third-party installers silently. Vendor wizards display their native UI and standard UAC prompts — in the UI *and* in headless mode.
8. **Direct Vendor Artifacts**: Downloads point exclusively to official vendor release infrastructure (GitHub Releases, Amazon S3, Azure CDN, HashiCorp Releases). No proxy mirrors, no intermediary repackaging, no modified binaries.
9. **Isolated User Sandbox**: Portable tools extract strictly into `%LOCALAPPDATA%\DevOpsToolsInstaller\`. System-wide directories (`Program Files`, `Windows\System32`) and Machine PATH (`HKLM`) are never touched without standard vendor installer elevation.
10. **Zero Telemetry**: No analytics, no user tracking, no phone-home pings, and no background daemon services. The audit log never leaves your machine.

---

## In-App Auto-Updater & Tray

DevOpsToolsInstaller includes an integrated, zero-friction updater:
- Automatically checks the GitHub Releases API on launch (toggleable in Settings) and on-demand via **Settings → Check for Updates**.
- A **background scheduler** re-checks every 30 minutes and surfaces new tool updates as a native `InfoBadge` counter on the **Installed** navigation item — plus a toast — only when the count changes.
- Displays release notes, version comparisons, asset sizes, and SHA-256 verification in a native Fluent dialog.
- Downloads the new release with real-time progress, verifies integrity, safely stages the update, and restarts smoothly.
- The **system tray icon** keeps the app one click away: quick actions for the catalog and update checks, and a true Exit that bypasses close-to-tray.

---

## Installation & Deployment Options

DevOpsToolsInstaller provides official distribution formats available from [GitHub Releases](https://github.com/NotHarshhaa/DevOpsToolsInstaller/releases/latest):

### ⚡ Option 1: Windows Package Manager (WinGet)
Install directly from your terminal using native Windows Package Manager:
```powershell
winget install DevOpsToolsInstaller
```
*(or explicitly by package ID: `winget install --id NotHarshhaa.DevOpsToolsInstaller`)*

### 🧙 Option 2: Windows Setup Wizard (Recommended)
Download **`DevOpsToolsInstaller_x64_Setup.exe`**:
- **App-branded Fluent wizard** with custom welcome page and logo.
- **Running-instance detection**: politely closes a running app (including tray-resident instances) before upgrading.
- **Optional PATH registration** and **clean uninstall** with an optional "remove my downloads, tools, and settings" prompt.
- Fully integrated with Windows Settings (*Installed apps* / *Programs and Features*).

### 🚀 Option 3: Portable Single-File Executable
Download **`DevOpsToolsInstaller_x64.exe`** (or `_arm64.exe` for ARM64 devices):
- Single self-contained executable with embedded compression.
- Completely portable: drop it onto a USB drive, `Downloads`, or `Desktop` and launch immediately with zero installation.

### 🖥️ Option 4: Scripted / Headless Provisioning
The portable executable doubles as a CLI for unattended setups:
```powershell
# Bootstrap a Kubernetes workstation from a provisioning script
.\DevOpsToolsInstaller.exe --install-bundle k8s-starter --install docker-desktop,vscode
if ($LASTEXITCODE -ne 0) { throw "Workstation provisioning failed" }
```

---

## Tech Stack

- **Framework**: WinUI 3 via Windows App SDK 1.6 (Mica Alt, CommandBar, InfoBadge, NavigationView)
- **Runtime**: .NET 8.0 (Self-Contained, Single-File Compressed)
- **Architecture**: MVVM with CommunityToolkit.Mvvm
- **Styling**: Windows 11 Fluent Design — system accent color, Mica Alt backdrop, Segoe Fluent Icons
- **Security**: Win32 `WinVerifyTrust`, .NET `ECDsa` (P-256) pinned-key catalog verification, SHA-256 integrity pipeline, Mark-of-the-Web tagging
- **Packaging**: Inno Setup 6 (branded setup wizard), MSIX, WinGet manifests, GitHub Actions release pipeline

---

## Catalog Format, Signing & Custom Tools

The catalog is stored in plain JSON (`catalog/catalog.json`) and refreshed dynamically from GitHub at runtime — **only when its ECDSA signature verifies against the pinned public key**; otherwise the embedded copy baked into the binary is used:

```jsonc
{
  "id": "terraform",
  "name": "Terraform",
  "category": "Infrastructure as Code",
  "description": "Infrastructure as Code tool for provisioning cloud resources",
  "iconGlyph": "\uE74C",
  "kind": "archive",
  "version": "1.9.5",
  "homepage": "https://www.terraform.io/",
  "downloadUrl": "https://releases.hashicorp.com/terraform/1.9.5/terraform_1.9.5_windows_amd64.zip",
  "fileName": "terraform_windows_amd64.zip",
  "sha256": "3a92...",
  "previousVersions": [ "1.8.5", "1.8.4" ]
}
```

To contribute a new tool:
1. Add the tool definition to [`catalog/catalog.json`](catalog/catalog.json).
2. Place the official vector SVG logo in [`src/DevOpsToolsInstaller/Assets/logos/<id>.svg`](src/DevOpsToolsInstaller/Assets/logos/).
3. (Optional) Reference the tool ID in relevant stacks within [`catalog/bundles.json`](catalog/bundles.json).

> [!IMPORTANT]
> Maintainers: after editing the catalog, run `catalog\sign-catalog.ps1` and commit the generated `.sig` files **together with** the JSON files — unsigned remote catalogs are rejected by shipped versions. The signing key is published to GitHub Actions as the `CATALOG_SIGNING_KEY` secret and signed automatically on every release.

---

## Troubleshooting

> [!WARNING]  
> If Microsoft Defender SmartScreen displays an *"Unrecognized app"* or *"Windows protected your PC"* notification on newly downloaded releases, this is expected behavior for open-source software before broad global reputation accumulates. Click **"More info"** → **"Run anyway"**, or verify the asset's cryptographic SHA-256 hash against `SHA256SUMS.txt`. (Files *downloaded by* DevOpsToolsInstaller are Mark-of-the-Web tagged on purpose — this is your protection working.)

- **CLI Tool Not Found in Terminal**: Ensure you have added `Tools\bin` to your PATH (via **Settings → Add to PATH**) and restarted your terminal session.
- **Offline / Unsigned Remote Catalog**: The app fails closed and uses the embedded catalog shipped in the binary. Catalog events (unreachable, unsigned) are recorded in the audit log.
- **Interrupted Download**: Just retry — the transfer resumes from where it stopped. A `<file>.partial` in the Downloads folder is expected while a download is incomplete.
- **Toast Notifications Not Appearing**: Delivery is best-effort on unpackaged desktop apps. Check **Settings → Toast Notifications** is on and that Windows notifications aren't focused/quiet for the app.
- **App "X" Closed But Still Runs**: Close-to-tray is on by default — look for the tray icon, or toggle the behavior off in **Settings → Close to System Tray**.

---

## FAQ

**Does this utility run installers silently or bypass UAC?**  
No. Vendor setup wizards run interactively and display their own standard elevation prompts — in the UI and in headless CLI mode. You remain in complete control of what runs on your workstation.

**Can I use this in an enterprise corporate environment?**  
Yes. The tool operates strictly within user space (`%LOCALAPPDATA%`), verifies Authenticode signatures with a configurable block policy, tags downloads for SmartScreen, writes a local audit trail, avoids modified binaries, and collects zero telemetry.

**Can I automate workstation provisioning with it?**  
Yes — that's what headless CLI mode is for: `--install`, `--install-bundle`, and `--status` with proper exit codes for scripting, MDT/Intune packaging, or CI jobs.

**What happens if someone tampers with the tool catalog?**  
Catalog updates are ECDSA-signed; shipped versions verify the signature against a pinned public key and **refuse unsigned or tampered catalogs**, falling back to the embedded copy.

**Where can I request new tools or stacks?**  
Submit an issue or open a pull request following the [Contributing Guidelines](CONTRIBUTING.md).

---

## Contributing

Contributions are warmly welcomed! Feel free to:
- Propose new DevOps, SRE, or cloud tools to [`catalog/catalog.json`](catalog/catalog.json).
- Submit new role-based stacks in [`catalog/bundles.json`](catalog/bundles.json).
- Improve existing download endpoints, versions, or SVG logos.
- Harden the security pipeline or extend the headless CLI surface.

Please review [CONTRIBUTING.md](CONTRIBUTING.md) before submitting pull requests.

---

## Acknowledgements

- Free code signing provided by the [SignPath Foundation](https://about.signpath.io/open-source/).

---

## License

This project is licensed under the [Apache-2.0 License](LICENSE).  
*All third-party tool trademarks, logos, and binaries belong to their respective copyright holders.*
