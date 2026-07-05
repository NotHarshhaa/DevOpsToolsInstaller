# DevOpsToolsInstaller

A native Windows desktop app built for DevOps engineers to quickly get
official installers for the tools of the trade — container runtimes,
IaC tools, Kubernetes CLIs, and cloud provider CLIs (AWS, Azure, GCP) —
all from one place.

DevOpsToolsInstaller does **not** install anything on its own. It downloads
the vendor's official, signed installer with a live progress bar, then
hands off to it so you complete setup exactly the way the vendor intended.
No silent installs, no bundled binaries, no background scripts modifying
your system.

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
3. DevOpsToolsInstaller downloads each installer directly from the vendor's official URL, with progress per item
4. Once downloaded, launch any installer with one click — it runs exactly as if you downloaded it yourself

Nothing is installed silently. Nothing runs with elevated permissions except
the vendor installer itself, which requests it the normal way.

## Categories covered

- **Cloud provider CLIs** — AWS CLI, Azure CLI, Google Cloud CLI, OCI CLI
- **Containerization** — Docker Desktop, Podman Desktop
- **Kubernetes** — kubectl, Helm, k9s, kind, minikube
- **Infrastructure as Code** — Terraform, Pulumi, Ansible
- **CI/CD & Version Control** — Git, GitHub CLI, GitLab CLI
- **Editors & Terminals** — VS Code, Windows Terminal
- **Utilities** — jq, yq, Postman

(Full, up-to-date list lives in [`catalog/catalog.json`](catalog/catalog.json).)

## Features

- 📦 Curated catalog of official DevOps tool and cloud CLI installers, grouped by category
- ⬇️ Concurrent downloads with real-time progress
- 🚀 One-click launch straight into the vendor's own installer
- 🔄 Catalog updates independently of the app — no reinstall needed for new tools
- 🧊 No bundled installers, no silent execution, no telemetry
- 🖥️ Native Windows 11 look and feel

## Installation

Download the latest release from the [Releases](../../releases) page and run
the `.exe`. No installation required — it's a single self-contained binary.

## Contributing

Contributions are welcome — especially catalog additions for new DevOps
tools and cloud CLIs, and corrections to download URLs. See
[CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

[Apache License](LICENSE)
