# DevOpsToolsInstaller

<p align="center">
  <em>One place to download every official DevOps tool installer for a fresh Windows workstation — without silent installs, bundled binaries, or telemetry.</em>
</p>

<p align="center">
  <img alt="Build" src="https://github.com/NotHarshhaa/DevOpsToolsInstaller/actions/workflows/release.yml/badge.svg" />
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" />
  <img alt="UI" src="https://img.shields.io/badge/UI-WinUI%203-2b579a" />
  <img alt="Arch" src="https://img.shields.io/badge/arch-x64%20%7C%20arm64-lightgrey" />
  <img alt="License" src="https://img.shields.io/badge/license-Apache--2.0-green" />
</p>

A native Windows desktop app built for DevOps engineers to quickly get
official installers and binaries for the tools of the trade — container
runtimes, IaC tools, Kubernetes CLIs, and cloud provider CLIs (AWS, Azure,
GCP) — all from one place.

DevOpsToolsInstaller does **not** install anything silently. It downloads
the vendor's official artifact with a live progress bar, then takes the
right hands-off action for what it downloaded — launching a vendor
installer, unpacking an archive, or dropping a CLI into a tools folder — so
you finish setup exactly the way the vendor intended. No bundled binaries,
no background scripts modifying your system.

---

## Contents

- [Why](#why)
- [Who this is for](#who-this-is-for)
- [How it works](#how-it-works)
- [What happens after download](#what-happens-after-download)
- [A quick tour](#a-quick-tour)
- [Categories covered](#categories-covered)
- [Adding `Tools\bin` to your PATH](#adding-toolsbin-to-your-path)
- [Features](#features)
- [Security & privacy](#security--privacy)
- [Installation](#installation)
- [Tech stack](#tech-stack)
- [Catalog format](#catalog-format)
- [Adding a tool to the catalog](#adding-a-tool-to-the-catalog)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

## Why

Setting up a new DevOps workstation usually means the same ritual every
time: open a dozen tabs, hunt down the AWS CLI installer, the Terraform
binary, kubectl, the Azure CLI, Helm, k9s, gcloud... and run each one by
hand. DevOpsToolsInstaller collects those links into a single, versioned
catalog so you can browse by category, queue up what you need for a new
machine, and download everything in one sitting — while the actual
installation stays fully in your hands, exactly like doing it manually.

## Who this is for

DevOps engineers, SREs, and platform engineers setting up or rebuilding
a Windows workstation who are tired of manually chasing down installer
links for the same set of tools every time.

## How it works

1. Browse the catalog, grouped by category (Cloud CLIs, Containers, IaC, Kubernetes, CI/CD, Editors, etc.)
2. Select the tools you need
3. DevOpsToolsInstaller downloads each artifact directly from the vendor's official URL, with progress per item
4. Once downloaded, trigger the right action for each tool with one click

Nothing is installed silently. Nothing runs with elevated permissions except
a vendor installer that **you** launch, which requests elevation the normal way.

## What happens after download

Not every tool is a classic setup wizard. The catalog tags each tool with a
**kind**, and the app takes the matching action:

| Kind | Examples | Action | Button |
|------|----------|--------|--------|
| **Installer** | AWS CLI, Docker Desktop, Git, VS Code | Launches the vendor installer (it shows its own UAC prompt) | `Install` |
| **Archive** | Terraform, Helm, Pulumi, glab | Extracts the `.zip` into a per-tool folder and opens it | `Extract` |
| **Binary** | kubectl, kind, jq, yq | Copies the standalone `.exe` into a single `Tools\bin` folder you can add to `PATH` | `Add to Tools` |
| **Script** | OCI CLI install script | Opens the folder so you can review it first — scripts are **never** run automatically | `Open Folder` |

If a catalog entry omits `kind`, the app infers it from the file extension
(`.zip` → archive, `.ps1` → script, `.msi`/`.exe` → installer), so older
catalogs keep working.

### Removing a tool

Every tool you've actioned can be removed again from the **Downloads** screen —
each row has an `Uninstall` / `Remove` button that does the right thing for the
tool's kind:

| Kind | Remove action | Button |
|------|---------------|--------|
| **Installer** | Finds the tool in the Windows uninstall registry and launches the **vendor's own uninstaller** (it shows its own prompts / UAC — nothing is removed silently) | `Uninstall` |
| **Archive** | Deletes the extracted `Tools\<tool-id>` folder | `Remove` |
| **Binary** | Deletes the standalone `.exe` from `Tools\bin` | `Remove` |
| **Script** | Nothing was installed, so only the cached download is removed | `Delete` |

The button only lights up when there's actually something to remove — the app
checks for the extracted folder, the copied binary, or a matching Windows
uninstall entry when the Downloads page loads. Every removal asks for
confirmation first and, by default, also clears the cached download so you get
disk space back. Just like installing, the app never elevates itself and never
runs a vendor uninstaller silently.

### Where files go

- **Downloads** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Downloads`
- **Extracted archives** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\<tool-id>`
- **Standalone binaries** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin`

## A quick tour

The app is built around four primary sections, accessible via the left navigation pane:

- **Home** — a dashboard showing catalog metrics (total tools available, downloaded tools count, and storage used), quick action links (Browse Catalog, View Downloads, Open Tools Folder), and featured preset stacks for rapid onboarding.
- **Tool Catalog** — the interactive directory of 47+ tools:
  - **Dynamic Search & Filtering**: Real-time search across tool names, categories, and descriptions; category filter chips with tool counts; and a "Downloaded only" toggle.
  - **Sort & Organize**: Sort alphabetically (A→Z, Z→A), by category, by kind, by downloaded status, or by your personal favorites.
  - **Favorites**: Star (☆/★) frequently used tools to keep them pinned and prioritized.
  - **Curated Preset Stacks**: One-click selection of specialized stacks (Kubernetes Core, Cloud Foundation, DevOps Essentials, Security & Scanning, CI/CD & Git, Infrastructure as Code).
  - **Profile Import & Export**: Export your tool selections to a `.json` profile file to share with teammates or replicate setups across workstations; import anytime to select tools automatically.
  - **Tool Specifications Modal**: Click info on any tool card to inspect technical specifications, copy official download URLs, and launch vendor documentation.
  - **Adaptive Responsive CommandBar**: Automatically reorganizes its controls across wide, laptop, snapped half-screen, and compact window sizes using an intelligent custom `WrapPanel` so options and search are never clipped.
- **Downloads** — live download and installation manager:
  - Concurrent downloads (up to 3 simultaneous items) with real-time speed, ETA, and progress metrics.
  - Context-aware post-download actions (`Install`, `Extract`, `Add to Tools`, or `Open Folder`).
  - Safe uninstallation and removal (`Uninstall` via vendor uninstaller, or `Remove` for extracted folders and binaries) with disk cleanup prompts.
- **Settings** — customize themes (Light / Dark / System Default), monitor disk usage, clear download caches, open local storage folders, verify user `PATH` configuration, and check catalog sync status.

## Categories covered

- **Cloud provider CLIs** — AWS CLI, Azure CLI, Google Cloud CLI, OCI CLI
- **Containerization** — Docker Desktop, Podman Desktop
- **Kubernetes** — kubectl, Helm, k9s, kind, minikube
- **Infrastructure as Code** — Terraform, OpenTofu, Pulumi, Ansible
- **CI/CD & Version Control** — Git, GitHub CLI, GitLab CLI, ArgoCD CLI
- **Security & Scanning** — Trivy, Grype, Syft, Cosign, Snyk CLI
- **Editors & Terminals** — VS Code, Windows Terminal
- **Utilities** — jq, yq, Postman, curl

(Full, up-to-date list lives in [`catalog/catalog.json`](catalog/catalog.json).)

## Adding `Tools\bin` to your PATH

Every **Binary** tool (kubectl, kind, jq, yq, …) lands in one folder. Add it
to your user `PATH` once and those CLIs work from any terminal. The app tells
you whether the folder is already on your `PATH`; to add it yourself, run this
in PowerShell (no admin required — it only touches your user environment):

```powershell
$bin  = "$env:LOCALAPPDATA\DevOpsToolsInstaller\Tools\bin"
$user = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($user -notlike "*$bin*") {
    [Environment]::SetEnvironmentVariable("PATH", "$user;$bin", "User")
    Write-Host "Added $bin to your user PATH. Restart your terminal to pick it up."
}
```

## Features

### 📦 Curated & Verified Catalog
- **47+ Official DevOps Tools**: Complete coverage of cloud CLIs, container engines, Kubernetes tooling, IaC frameworks, CI/CD runners, security scanners, editors, and CLI utilities.
- **Direct Vendor Downloads**: All artifacts are fetched directly from official release URLs (GitHub releases, vendor CDNs, official MSI packages) — zero repackaging or proxied files.
- **Independent Catalog Updates**: Fetches the newest catalog directly from GitHub on startup with an embedded offline fallback when disconnected.

### 🎯 Smart Selection & Preset Stacks
- **Curated Presets**: Quick-select battle-tested stacks with one click:
  - *Kubernetes Core*: kubectl, Helm, k9s, kind, minikube
  - *Cloud Foundation*: AWS CLI, Azure CLI, Google Cloud CLI, OCI CLI
  - *DevOps Essentials*: Git, Docker Desktop, Terraform, VS Code, jq
  - *Security & Scanning*: Trivy, Grype, Syft, Cosign, Snyk CLI
  - *CI/CD & Git*: Git, GitHub CLI, GitLab CLI, ArgoCD CLI
  - *Infrastructure as Code*: Terraform, OpenTofu, Pulumi, Ansible
- **Profile Import & Export**: Save your exact tool selection as a portable JSON profile (`.json`) to standardize team environments or restore setups on new machines.
- **Favorites System**: Pin preferred tools with a single click (☆/★) to sort or filter favorites first.
- **Bulk Selection Controls**: Instant "Select All" and "Clear" controls.

### ⚡ Intelligent Download & Lifecycle Management
- **Concurrent Download Engine**: Downloads up to 3 artifacts simultaneously with per-item progress, transfer speed, and ETA tracking.
- **Artifact-Aware Actions**:
  - *Installers (`.msi`, `.exe`)*: Launches the official vendor installer with its standard UAC prompt.
  - *Archives (`.zip`)*: Extracts directly into `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\<tool-id>`.
  - *Standalone Binaries (`.exe`)*: Copies the CLI executable into `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin`.
  - *Scripts (`.ps1`, `.sh`)*: Safely opens the folder for user inspection before execution.
- **Safe, Reversible Uninstallation**: Cleanly removes tools from within the app:
  - Launches the vendor's official uninstaller via Windows Registry detection.
  - Deletes extracted archive folders and standalone binaries.
  - Prompts to clean up cached installer files to reclaim disk space.
- **SHA-256 Checksum Verification**: Automatically validates downloaded file integrity against catalog hashes.

### 🔍 Search, Sorting & Inspection
- **Instant Search**: Real-time filtering across tool names, categories, and descriptions.
- **Flexible Sorting**: Sort by Name (A→Z, Z→A), Category, Artifact Kind, Downloaded Status, or Favorites.
- **Detailed Tool Specs**: Inspect full tool metadata, copy download links to clipboard, and open official documentation sites.

### 💻 Modern Windows 11 Native Experience
- **Fluent Design & WinUI 3**: Native controls adhering to Windows 11 styling guidelines, clean typography, theme resources, and no distracting neon badges.
- **Adaptive Layout**: Custom `WrapPanel` and dynamic responsive breakpoints that adjust search bars, buttons, and icons smoothly from 4K monitors down to snapped half-screen windows.
- **Light / Dark / System Themes**: Instant theme switching remembered across restarts.
- **Self-Contained & Lightweight**: Native x64 and ARM64 executables with zero runtime dependencies.
- **Zero Telemetry**: No analytics, no tracking, and no background services.

## Security & privacy

- **No silent installs.** The app downloads artifacts; you decide when to run
  them. Installers surface their own UAC prompt — the app itself never
  requests elevation.
- **Scripts are never auto-executed.** For `script`-kind entries (e.g. the OCI
  CLI installer), the app only opens the containing folder so you can review
  the script before running it yourself.
- **Downloads come straight from the vendor.** URLs point at official vendor
  or first-party release hosts; nothing is proxied or repackaged.
- **Optional integrity checks.** When a catalog entry includes a `sha256`, the
  downloaded file is hashed and re-downloaded on mismatch.
- **No telemetry.** The only network calls are fetching the catalog JSON and
  downloading the artifacts you choose.

## Installation

Download the latest release from the [Releases](../../releases) page and run
the `.exe`. No installation required — it's a single self-contained binary.
Builds are published for both **x64** and **arm64**.

## Tech stack

- **WinUI 3** (Windows App SDK) on **.NET 8**
- Self-contained, no runtime install required
- Catalog is plain JSON — no code changes needed to add tools

## Catalog format

The catalog is a flat JSON array. Each entry describes one tool:

```jsonc
{
  "id": "terraform",                 // unique, stable identifier
  "name": "Terraform",               // display name
  "category": "Infrastructure as Code",
  "description": "Infrastructure as Code tool for provisioning cloud resources",
  "iconGlyph": "\uE74C",             // Segoe Fluent Icons glyph
  "kind": "archive",                 // installer | archive | binary | script
  "version": "1.9.5",                // optional, shown in the UI
  "homepage": "https://www.terraform.io/",  // optional
  "downloadUrl": "https://releases.hashicorp.com/terraform/1.9.5/terraform_1.9.5_windows_amd64.zip",
  "fileName": "terraform_windows_amd64.zip",
  "sha256": "",                      // optional; if set, verified after download
  "launchArgs": ""                   // optional args passed to an installer
}
```

- `kind` drives the post-download action (see [What happens after download](#what-happens-after-download)).
  If omitted, it's inferred from the file extension for backward compatibility.
- `sha256`, when provided, is checked after download; a mismatch triggers a re-download.

## Adding a tool to the catalog

1. Add a new object to [`catalog/catalog.json`](catalog/catalog.json) with the fields above.
2. Pick a unique, stable `id` (used for the per-tool extract folder).
3. Set the correct `kind` so the app takes the right action:
   - `installer` for `.exe`/`.msi` setups that install themselves
   - `archive` for `.zip` files that need extracting
   - `binary` for a single standalone `.exe` (a CLI)
   - `script` for `.ps1`/shell scripts the user should review first
4. Point `downloadUrl` at the **official** vendor artifact and set a matching `fileName`.
5. (Recommended) fill in `version` and `homepage`; (optional) add a `sha256`.
6. Rebuild, or just let the app pick up the change on next launch — the
   catalog is fetched from GitHub at runtime with the embedded copy as a fallback.

## Troubleshooting

- **A download fails or stalls.** Re-select the tool and download again; the
  app removes partial files on failure and resumes from a clean state. Check
  that the vendor URL in the catalog is still current.
- **"Add to Tools" worked but the CLI isn't found.** Make sure
  `Tools\bin` is on your `PATH` (see
  [above](#adding-toolsbin-to-your-path)) and restart your terminal.
- **The catalog looks out of date.** The app fetches the catalog from GitHub
  on launch and falls back to the embedded copy when offline. Reconnect and
  relaunch to get the latest.
- **Theme didn't change everything.** Theme is applied live; if a screen looks
  off, navigate away and back.

## FAQ

**Does it install tools silently or with admin rights?**
No. It downloads artifacts and then hands off to the vendor's own installer,
which you launch and which shows its own UAC prompt if it needs one.

**Will it run the OCI install script for me?**
No. Script-kind entries are downloaded and the folder is opened for review;
you run the script yourself.

**Where are my downloads and extracted tools?**
Under `%LOCALAPPDATA%\DevOpsToolsInstaller` — see
[Where files go](#where-files-go).

**Can I add my own tools?**
Yes — see [Adding a tool to the catalog](#adding-a-tool-to-the-catalog).

## Roadmap

Ideas under consideration (contributions welcome):

- SHA-256 hashes populated for more catalog entries
- Per-tool "update available" hints when a newer version ships
- A one-click "Add `Tools\bin` to PATH" action inside Settings
- Support for `.tar.gz`/`.7z` archive extraction

## Contributing

Contributions are welcome — especially catalog additions for new DevOps
tools and cloud CLIs, and corrections to download URLs and versions. When
adding a tool, set the correct `kind` so the app takes the right action.
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

[Apache License](LICENSE)
