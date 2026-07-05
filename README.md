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
- [Build from source](#build-from-source)
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

### Where files go

- **Downloads** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Downloads`
- **Extracted archives** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\<tool-id>`
- **Standalone binaries** — `%LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin`

## A quick tour

The app has four screens, reachable from the left navigation pane:

- **Home** — a summary of how many tools are in the catalog and how many
  you've already downloaded, plus quick links into the rest of the app.
- **Tool Catalog** — the full, searchable list grouped by category. Each row
  shows the tool's name and version, a **kind** badge (Installer / Archive /
  Binary / Script), and its category. Select any number of tools and hit
  **Download Selected** to fetch them concurrently.
- **Downloads** — live progress for everything you've queued, with a
  context-aware action button per tool (`Install`, `Extract`, `Add to Tools`,
  or `Open Folder`).
- **Settings** — switch between Light / Dark / System themes, see how much
  disk your downloads use, clear the download cache, and open the downloads
  folder.

## Categories covered

- **Cloud provider CLIs** — AWS CLI, Azure CLI, Google Cloud CLI, OCI CLI
- **Containerization** — Docker Desktop, Podman Desktop
- **Kubernetes** — kubectl, Helm, k9s, kind, minikube
- **Infrastructure as Code** — Terraform, Pulumi, Ansible
- **CI/CD & Version Control** — Git, GitHub CLI, GitLab CLI
- **Editors & Terminals** — VS Code, Windows Terminal
- **Utilities** — jq, yq, Postman

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

- 📦 Curated catalog of official DevOps tool and cloud CLI installers, grouped by category
- 🧠 Artifact-aware actions — installers launch, archives extract, binaries go to a PATH-able folder, scripts open for review
- ⬇️ Concurrent downloads (up to 3 at a time) with real-time per-item progress
- ✅ Optional SHA-256 verification — a mismatch triggers an automatic re-download
- 🔎 Instant search across tool names, categories, and descriptions
- 🌗 Light / Dark / System-default themes, remembered between sessions
- 🔄 Catalog updates independently of the app — no reinstall needed for new tools (fetched from GitHub, with an offline embedded fallback)
- 🧊 No bundled installers, no silent execution, no telemetry
- 🖥️ Native Windows 11 look and feel (WinUI 3), for both x64 and arm64

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

## Build from source

Requires the **.NET 8 SDK** on Windows 10/11.

```powershell
# Release build (self-contained folder output)
.\build.ps1

# Build and launch
.\build.ps1 -Run

# Single-file executable
.\build.ps1 -SingleFile

# Clean all build artifacts
.\build.ps1 -Clean
```

Or with the SDK directly:

```powershell
dotnet build src/DevOpsToolsInstaller/DevOpsToolsInstaller.csproj -c Release
```

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
