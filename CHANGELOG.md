# Changelog

All notable changes to **DevOps Tools Installer** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Changed
- **Windows App SDK 2.5.1**: Upgraded the app framework from Windows App SDK 1.6 to the latest stable 2.x release (2.5.1). Minimum OS (Windows 10 1809) remains unchanged — 2.x still supports it.
- **SDK projection update**: Moved the TFM projection band to `net8.0-windows10.0.26100.0` and removed the stale `WindowsSdkPackageVersion 10.0.19041.38` pin to resolve the `WinRT.Runtime 2.1 vs 2.2` assembly conflict introduced by the Toolkit/WinUI 2.x references. Minimum OS stays Windows 10 1809.
- **Built-in TitleBar control**: Replaced the hand-rolled custom title bar grid with the Windows App SDK `Microsoft.UI.Xaml.Controls.TitleBar` control (icon, title, version badge, automatic drag regions and caption-button spacing).
- **Modern toast notifications**: Migrated `ToastService` from the legacy `Windows.UI.Notifications` template API to the Windows App SDK app-notifications API (`Microsoft.Windows.AppNotifications` / `AppNotificationBuilder`), which also registers the AUMID automatically for unpackaged (Inno Setup / MSI) installs.
- **MSIX manifest**: Bumped `MaxVersionTested` to Windows 11 24H2 (10.0.26100.0) to reflect the validated 2.x runtime.
- **Release pipeline**: Fixed stale MSI version fallback in `release.yml` (2.9.0 → 3.0.0).
- **Settings page redesign**: Rebuilt `SettingsPage` with the Windows Community Toolkit `SettingsCard` / `SettingsExpander` controls (`CommunityToolkit.WinUI.Controls.SettingsControls` 8.2.251219, verified compatible with Windows App SDK 2.5.1) to match the Windows 11 Settings app. Groups: Appearance, Security, Notifications & Tray, Network, PATH & Terminal, Storage & Shortcuts, Updates, About. All bindings, handlers and behaviors are unchanged; added icons, descriptions, a persistent warning InfoBar for the unsigned-installer policy, and `AutomationProperties` names throughout for keyboard/Narrator accessibility.

---

## [3.0.0] - 2026-09-30

### Added
- **Workstation Migration & Package Import**:
  - Discover and import existing developer packages installed via `winget` or `chocolatey` directly into DevOpsToolsInstaller (`--import-winget`, `--import-choco`, or via UI buttons).
  - Automatically map imported binaries to managed catalog entries.
- **Workstation Health & Audit Reporting**:
  - Export a full Markdown workstation audit report (`--export-report [file]` and UI button) detailing installed tools, versions, environment PATH health, and catalog status.
- **Tool Profile Synchronization**:
  - Export and import workstation tool profiles (`--export-profile <file>`, `--import-profile <file>`) to rapidly replicate setups across machines.
- **Standalone Bootstrap Generator**:
  - Generate a portable, self-contained PowerShell bootstrap script (`--generate-bootstrap <file>`) that unattendedly provisions developer workstations from scratch with zero prerequisites.
- **Corporate Proxy & Enterprise Network Engine**:
  - Custom HTTP, HTTPS, and SOCKS proxy configuration with optional basic authentication under **Settings → Network**.
  - All downloads, catalog updates, and GitHub release checks seamlessly route through corporate proxies.
- **Fluid Motion Helpers & WinUI 3 Polish**:
  - Motion class with attached properties for `HoverLift` elevations and interactive `Pulse` animations on cards and buttons.
- **Tool Release Notes Viewer**:
  - In-app modal dialog to inspect upstream release notes and changelogs before updating or installing.
- **Microsoft Store Availability**:
  - Official distribution via Microsoft Store with automatic background updates (`winget install 9PJL0VR3H7VG` or [Store Listing](https://apps.microsoft.com/detail/9PJL0VR3H7VG)).
- **Automated Catalog Refresh Workflow**:
  - Scheduled GitHub Action (`catalog-refresh.yml`) and `refresh-catalog.ps1` script to keep tool versions and hashes updated weekly.

### Changed
- **Extended Headless CLI**: Added `--update-all`, `--update <tools>`, and `--download-only <tools>`.
- **WinGet Workflow**: Updated `.github/workflows/winget.yml` to trigger exclusively via manual dispatch (`workflow_dispatch`).
- **Documentation**: Added Microsoft Store badges, direct store links, and updated CLI provisioning instructions in `README.md`.

---

## [2.9.0] - 2026-09-28

### Added
- **Native Tar Archive Support**:
  - Added support for `.tar.gz`, `.tar.bz2`, `.tar.xz`, `.tgz`, and `.tar` extractions using Windows built-in `tar.exe` (bsdtar).
- **Extended Headless CLI Commands**:
  - Added `--uninstall <tools>` to scriptably remove standalone binaries and launch vendor uninstallers.
  - Added `--version` to output the runtime version string.
- **Full-Catalog Health Checks**:
  - Expanded mapped CLI probes to 79 tools across the catalog for accurate version detection and health status.

### Security
- **Fail-Closed Authenticode Verification**:
  - Only positively verified Authenticode signatures pass the "Block unsigned" security policy; undetermined signature checks (`Unknown`) are treated as untrusted.
  - Full certificate chain revocation validation (`WTD_REVOKE_WHOLECHAIN`) with explicit CRL/OCSP error handling.
- **Resilient Resumable Download Engine**:
  - Added automatic exponential backoff retry for transient network dropouts.
  - ETag-validated resume: stale `.partial` downloads are discarded if the remote asset has changed upstream.

---

## [2.8.0] - 2026-09-24

### Added
- **Cryptographic Catalog Signing**:
  - Implemented `CatalogSignatureService` with publisher-pinned ECDSA P-256 public key verification.
  - Shipped binaries reject tampered or unsigned remote catalogs and fall back cleanly to embedded definitions.
- **Security & Integrity Pipeline**:
  - Added Authenticode signature verification (`WinVerifyTrust`) with configurable Warn/Block policies.
  - Added persistent append-only daily audit logs in `%LOCALAPPDATA%`.
  - Automatic Windows Mark-of-the-Web (`Zone.Identifier`) tagging on all downloaded binaries.
- **Headless CLI & System Tray**:
  - Introduced headless command-line interface (`--install`, `--install-bundle`, `--status`, `--list`).
  - Added system tray icon with quick actions and minimize-to-tray capability.
- **Native Windows 11 Fluent UI**:
  - Integrated native `CommandBar` toolbars and system accent color adaptation.
  - Added custom-branded Inno Setup wizard visuals and artwork.

---

## [2.5.0] - 2026-09-21

### Added
- **Enterprise Windows Installer (.msi)**:
  - Added WiX Toolset v5 setup project (`setup.wxs`) for enterprise GPO, Intune, and silent deployment.
- **MSIX Packaging for Microsoft Store**:
  - Packaged desktop bridge build with `Package.appxmanifest` and automated `-Msix` build script.
- **Windows Setup Wizard**:
  - Added Inno Setup 6 branded wizard with running-instance detection, automated shortcuts, and PATH registration.
- **Downloads & Queue Management**:
  - Real-time download speed tracking, remaining time estimates, and File Explorer folder navigation.
- **Installed Tools & Environment Management**:
  - Installed tools dashboard with system PATH health validation service.
  - Ability to add/remove `Tools\bin` from the user's environment variables.
- **Privacy Policy**:
  - Added formal zero-telemetry, zero-data-collection privacy policy document (`PRIVACY.md`).

---

## [2.1.0] - 2026-09-20

### Added
- **Curated Tool Stacks & Role Presets**:
  - Added Stacks page with preset stacks for Kubernetes, Cloud Native, IaC, Observability, and DevSecOps.
  - Support for multi-tool batch downloads and installations.
- **Application Update Checker**:
  - Added GitHub release update checking with in-app notification banner and dialog.
- **Enhanced Asset Compression**:
  - Enabled single-file compression for portable binary distribution.
- **Catalog Expansion**:
  - Expanded catalog to over 90 developer and cloud CLI tools with official vector SVG logos.

---

## [2.0.0] - 2026-09-19

### Added
- **Tool Version Selector**:
  - Ability to browse and select previous release versions directly from tool cards.
- **CLI Health Checks**:
  - Probing mechanism to verify whether a tool is installed, accessible in PATH, and healthy.
- **Favorites & Activity Logging**:
  - Pin frequently used tools as favorites.
  - Local activity log tracking downloads and installation events.
- **Desktop Integration**:
  - Automated desktop and Start Menu shortcut creation.
  - Added dedicated About & Diagnostic page.
- **UI Modernization**:
  - Redesigned Catalog page layout with fluid responsive grid wrapping.

---

## [1.5.0] - 2026-09-19

### Added
- **Expanded Cloud & CLI Catalog**:
  - Added tools for AWS, Azure, Google Cloud, and container ecosystems.
  - Improved SVG logo rendering and high-DPI scaling.
- **Download Settings**:
  - Configurable download directories and retry options.
- **Release Automation**:
  - Script for automated milestone release notes generation (`generate-release-notes.ps1`).

---

## [1.2.0] - 2026-07-06

### Added
- **Uninstall Engine**:
  - Initial uninstallation functionality for standalone extracted tools and user binaries.
- **Catalog Loading Performance**:
  - Asynchronous background catalog loading to prevent UI thread stuttering.
- **Community Documentation**:
  - Added `CONTRIBUTING.md` guidelines for catalog contributions.

---

## [1.1.0] - 2026-07-05

### Added
- **Redesigned Settings**:
  - Revamped Settings page with categorized navigation and clean card layouts.
- **Welcome & Onboarding**:
  - Added hero banner and workstation status indicators.
- **CI/CD Enhancements**:
  - Optimized GitHub Actions release pipeline.

---

## [1.0.0] - 2026-07-05

### Added
- **Initial Release of DevOps Tools Installer**:
  - Native Windows 11 workstation provisioning app built with WinUI 3 and .NET 8.
  - Curated catalog of essential developer tools, container runtimes, and cloud CLIs.
  - Light and Dark Fluent Design theme support following system appearance.
  - Direct upstream downloads with SHA-256 hash validation.
  - Automated GitHub Actions build and release workflow.
