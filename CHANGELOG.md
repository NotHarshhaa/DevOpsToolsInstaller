# Changelog

All notable changes to **DevOps Tools Installer** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Workstation Profiles (`ProfilesPage` → Profiles, `Services/ProfileStoreService.cs`)**:
  - Capture the currently installed toolset, or the current catalog selection, as a named, reusable profile (with optional description).
  - **Apply** a profile to the catalog selection, or **Install** every tool it references in one click (respects off-peak scheduling).
  - Portable **Import/Export** of profiles as JSON; stored in `%LOCALAPPDATA%\DevOpsToolsInstaller\profiles.json`.
- **Workstation Snapshots (`ProfilesPage` → Snapshots, `Services/SnapshotService.cs`)**:
  - Capture point-in-time restore points of installed tools, their versions, and User PATH.
  - **Diff** a snapshot against the current workstation (missing / version-changed / added) and **Restore** missing tools with confirmation.
- **Install History & Rollback (`ProfilesPage` → History, `Services/InstallHistoryService.cs`)**:
  - Persistent JSONL log (`history.jsonl`) of every install, update, uninstall, and rollback, with a severity-badged timeline and one-click clear.
  - Guided **Roll back a tool…** flow that re-downloads and reinstalls a previously recorded version and records the rollback.
- **Cloud-Synced Profiles (`ProfilesPage` → Sync, `Services/ProfileSyncService.cs`)**:
  - Opt-in **Sync / Push / Pull** of profiles, snapshots, hooks, and custom tools through a user-chosen folder (OneDrive, Dropbox, network share) — merge-by-most-recent, no account or server.
- **Per-Tool Pre/Post-Install Hooks (`Services/HookService.cs`)**:
  - Opt-in, user-authored pre-install and post-install shell commands, run around a tool's artifact action.
  - Global master switch in **Settings → Hooks & Automation** plus a per-tool enable toggle and command editors on the tool detail page, with **Test pre / Test post** actions.
  - Commands support `{tool}`, `{toolId}`, `{version}`, `{file}`, `{downloadsFolder}`, `{toolsBin}`, and `{toolsRoot}` placeholders; every command is written to the activity & audit log before it runs — never silent, and off by default.
  - Stored in `%LOCALAPPDATA%\DevOpsToolsInstaller\hooks.json`.
- **In-App Custom Tool Catalog Editor (`CatalogEditorPage`, `Services/CustomCatalogService.cs`)**:
  - Author, validate, edit, and delete custom catalog entries (id, kind, download URL, file name, SHA-256, logo, homepage, launch args).
  - Import/Export a custom `catalog.json` feed; custom entries are validated and merged into the live catalog so they behave exactly like shipped tools. Stored in `custom_tools.json`.
- **Headless CLI additions (`Services/CliHost.cs`)**: `--history [toolId]`, `--snapshot <name>`, `--restore-snapshot <name>`, `--profiles`, `--capture-profile <name>`, `--apply-profile <name|id>`, `--sync-profiles`, and `--run-hooks`.
- **Navigation**: new **Profiles & History** (`ProfilesPage`) navigation item, **Custom Tools** (`CatalogEditorPage`) footer item, and command-palette entries for both.

### Changed
- `ArtifactService.Perform` now runs pre/post-install hooks (when enabled) and records successful installs to the install history.
- `UninstallService.Uninstall` now records successful uninstalls to the install history.
- `CatalogService` now merges user-defined custom tools into the catalog on load (`LoadCatalogAsync`).

## [3.0.2] - 2026-10-13

### Added
- **HomePage Showcase Redesign & Core Features Grid**:
  - Built an immersive Hero header with in-app Acrylic material, value proposition statement, and primary **"Browse catalog"** and secondary **"Build a stack"** CTA buttons.
  - 3-step **Quick Start** horizontal stepper providing an intuitive workstation onboarding overview (*Pick tools* → *Verify & download* → *Install*).
  - 9-card responsive **Core Features Showcase** grid (3 columns wide, 2 medium, 1 narrow) using `ItemsRepeater` and `UniformGridLayout`.
  - Dynamic data bindings for real counts (*90+ tools*, *8 curated stacks*, *live installed count*).
  - Smooth hover and focus elevation lift (`helpers:Motion.HoverLift`) respecting the Windows *Reduce animations* accessibility setting.
  - Skeleton shimmer pulse placeholders (`helpers:Motion.Pulse`) during catalog data hydration.
  - First-run 3-step **TeachingTip** 30-second onboarding tour with skip and settings dismissal options.
  - Headless CLI documentation modal dialog with quick copy for unattended script commands.
  - Footer bar with links to GitHub repository, issue reporting, and official documentation.
- **Custom Stack Builder (`StacksPage`)**:
  - Interactive 2-column composer with a smooth Segmented switcher between "Presets" and "Stack Builder" (`ViewModels/StackBuilderViewModel.cs`, `Services/CustomStacksService.cs`).
  - Searchable catalog on the left with vector logos, badges, and keyboard-accessible **"+ Add"** buttons.
  - "My Stack" drop target on the right with drag-and-drop tool additions, drag reordering, and dedicated **Move Up** / **Move Down** controls.
  - Delete tools via dedicated remove buttons or by selecting an item and pressing <kbd>Delete</kbd>.
  - Stack naming and description inputs with live reactive metrics: **Total Tools Count** and **Estimated Download Size** (based on disk cache and package kinds).
  - Local persistence to `%LOCALAPPDATA%\DevOpsToolsInstaller\custom_stacks.json` automatically loaded into global presets.
  - Portable **Export (.json)** and **Import (.json)** with catalog validation (unknown or deprecated tool IDs are safely skipped and reported via `InfoBar`).
  - 1-click **"Install This Stack"** reusing batch downloads, concurrency limits, and off-peak scheduling.
  - Added **"Open in Stack Builder"** flyout action to all 12 curated preset cards for instant stack customization.
- **Workstation Doctor Page (`DoctorPage`)**:
  - Dedicated health & diagnostics page in `NavigationView` (`Views/DoctorPage.xaml`, `ViewModels/DoctorViewModel.cs`, `Services/DoctorService.cs`).
  - 5-category non-blocking scans: PATH conflicts & duplicates, missing prerequisites (WSL2, Hyper-V, virtualization), multi-version CLI conflicts, outdated tools vs. catalog, and installed tools missing from PATH.
  - Live scan status header with `ProgressRing` and count badges (`Passed`, `Warnings`, `Failed`).
  - Grouped findings with explanations and confirmed **Fix** actions that prompt for explicit user confirmation before applying system changes.
  - **Re-run checks** and **Copy report** (Markdown format) buttons with full Narrator and keyboard accessibility.
- **Global Collapsible Log Drawer (`LogDrawerControl`)**:
  - Universal bottom log drawer available across every page in the application (`Controls/LogDrawerControl.xaml`, `ViewModels/LogDrawerViewModel.cs`).
  - Slide-up toggle button and global <kbd>Ctrl</kbd>+<kbd>`</kbd> keyboard shortcut.
  - Drag-resizable height remembered across sessions in user settings.
  - Live streams download chunks, SHA-256 verifications, Authenticode `WinVerifyTrust` checks, and installation outcomes with timestamps and color-coded severity.
  - Virtualized `ListView` for smooth performance with thousands of entries.
  - Fluent `Segmented` severity filtering (`All`, `Info`, `Warning`, `Error`), live search filter, auto-scroll pause toggle, clear, and clipboard copy.
  - Respects app theme, system accent colors, and Windows *Reduce animations* accessibility settings.
- **PATH Environment Doctor & Fixer**:
  - Comprehensive inspection of User and Process PATH environment variables detecting duplicate entries, non-existent directories, malformed entries, and missing `Tools\bin` directory (`Services/PathDoctorService.cs`).
  - Automatic timestamped backups prior to any modifications stored in `%LOCALAPPDATA%\DevOpsToolsInstaller\Backups\PATH`.
  - 1-click "Clean & Fix" optimization in **Settings → PATH & Terminal** that prunes dead directories, deduplicates paths, ensures `Tools\bin` is configured, and broadcasts Windows environment updates (`WM_SETTINGCHANGE`).
  - One-click restore flyout to rollback User PATH to any previous timestamped state.
  - Headless CLI integration via `--path-doctor` (audit report) and `--fix-path` (safe cleanup and backup).
- **VS Code DevContainer & Dockerfile Generator**:
  - Export installed workstation toolchains or curated stacks into production-grade `.devcontainer/devcontainer.json` and multi-stage `Dockerfile` (`Services/DevContainerGeneratorService.cs`).
  - Automatically translates catalog tools to official DevContainer features (`ghcr.io/devcontainers/features/...` for kubectl, terraform, docker, cloud CLIs, etc.) and installs Linux CLI binaries (`k9s`, `trivy`, `sops`, `yq`, `starship`).
  - Generates recommended VS Code extension manifests and default zsh/bash configuration.
  - Export directly from **Installed Tools → Export → Export DevContainer & Dockerfile (.zip)** or **Curated Stacks → Stack menu → Export as DevContainer (.zip)**, as well as via headless CLI with `--generate-devcontainer <zip/folder>`.
- **Off-Peak Download Scheduler & Auto-Retry Engine**:
  - Configure scheduled off-peak download windows (e.g., 11:00 PM to 07:00 AM) under **Settings → Network** to preserve daytime network bandwidth (`Services/DownloadSchedulerService.cs`).
  - Background scheduler automatically tracks queued transfers in `ToolStatus.Scheduled` and executes them upon entering the active schedule window.
  - Interactive "Start All Now (Bypass Schedule)" action on `DownloadsPage` InfoBar for on-demand execution.
  - Dedicated "Scheduled" filter chip on `DownloadsPage`.
  - Automated exponential retry mechanism for failed downloads to recover from transient Wi-Fi drops or network disconnects.

### Fixed
- **Persistent Floating Keyboard Accelerator Badges**:
  - Set `KeyboardAcceleratorPlacementMode="Hidden"` on the root container to prevent WinUI 3 from displaying an undismissible floating "Ctrl+K" badge overlay on the window surface while retaining full shortcut functionality.

## [3.0.1] - 2026-10-11

### Added
- **HomePage Hero Banner & Overview Redesign**:
  - Rebuilt the top section of `HomePage` with an isometric modern DevOps hero banner illustration (`Assets/HeroBanner.jpg`).
  - Added seamless gradient masking, version tag badge, primary "Browse Catalog" CTA button, and quick-action navigation pills ("Curated Stacks", "Installed Tools", "Download Queue").
- **Catalog Quick Status Filter & Selection Presets**:
  - Filter catalog tools instantaneously with the Fluent status filter: `All`, `Not Installed`, `Installed`, `Updates Available`, and `Favorites Only`.
  - Batch selection presets added to catalog selection bar: "Select All Not Installed" and "Select All with Updates Available".
- **Download Bandwidth Throttling / Speed Limiter**:
  - Configurable download speed cap under **Settings → Network** (`Unlimited`, `2 MB/s`, `5 MB/s`, `10 MB/s`, `25 MB/s`, `50 MB/s`).
  - Integrated into `DownloadService` using a high-precision token rate limiter to avoid saturating network connections during artifact downloads.
- **Custom / Private Tool Catalog Feed**:
  - Support for custom JSON catalog URLs (HTTPS or local files) configured in **Settings → Updates**.
  - Secure loading in `CatalogService` with automatic fail-closed fallback to the standard catalog if the custom feed is unreachable or invalid.
- **Workstation Profile & PowerShell Bootstrap Export from Installed Page**:
  - One-click export dropdown menu on Installed page offering Markdown Audit Report, JSON Workstation Profile, and Standalone PowerShell Bootstrap Script.
- **Tool Detail Page direct download copy**:
  - Added dedicated Direct Download URL card with one-click clipboard copy on `ToolDetailPage`.

### Performance & UI Smoothness (Lag Reduction)
- **Eliminated Search and Filter Lag**:
  - Added 150ms debouncing across all search and filter input fields (`CatalogPage`, `InstalledPage`, `DownloadsPage`, and TitleBar search box) to eliminate UI thread lockup on fast typing.
  - Implemented smart in-place collection diffing instead of clearing and re-adding entire collections on filter changes, avoiding mass visual re-renders and scroll resets.
- **Zero-Stutter Grid Scrolling & Layout**:
  - Removed staggered entrance transitions (`EntranceThemeTransition IsStaggeringEnabled="True"`) on catalog `GridView` item containers which caused animation frame drops on large collections.
  - Pre-cached local SVG/PNG tool logos in memory (`HashSet<string>`) at startup to eliminate synchronous disk I/O (`Directory.Exists` / `File.Exists`) on the UI thread during scrolling.
  - Restricted logo bitmap decoding to `DecodePixelWidth/Height = 48` to reduce GPU texture memory overhead.
  - Added threshold guards to `GridView_SizeChanged` to prevent layout thrashing on sub-pixel window resizing.
- **Startup & Navigation Optimization**:
  - Eliminated redundant page instantiation during startup navigation (navigates directly to `SettingsService.LastPage` without instantiating and immediately discarding `HomePage`).
  - Disabled chart animation interpolation (`AnimationsSpeed = TimeSpan.Zero`) on telemetry sparklines to eliminate CPU interpolation frame drops on high-frequency timers (250ms–1s).
  - Bounded background version probing on `InstalledPage` to a concurrent throttle of 3 tasks via `SemaphoreSlim`, with immediate cooperative cancellation on page unload.
- **Window, tray & onboarding improvements**:
  - **WinUI-style tray menu**: The native tray icon now opens a Fluent `MenuFlyout` (Open, Open Tool Catalog, Check for tool updates, Exit) hosted in a borderless flyout window at the cursor — keyboard and Narrator accessible. (H.NotifyIcon.WinUI 2.4.1/2.3.1 was evaluated but its WinRT projection does not bind against Windows App SDK 2.5.1 on net8.0, so the tray icon stays native and only the menu moved to WinUI.)
  - **Session persistence**: Window size/position and the last visited page are saved to `settings.json` and restored on launch.
  - **Splash screen**: A borderless, centered splash shows at startup and closes when the main window activates.
  - **TitleBar search**: The global search box moved into the built-in TitleBar (version shown as its Subtitle).
  - **Ctrl+K command palette**: Jump to any page, install any tool, or open the update checker from a searchable palette.
  - **First-run onboarding**: Three TeachingTips (search/Ctrl+K, Installed page, tray behavior), shown once, dismissible, and toggleable in Settings → Notifications & Tray ("First-run onboarding tips").
- **In-app Mica/Acrylic surfaces (Windows App SDK 2.x `SystemBackdropElement`)**: The NavigationView sidebar pane and custom flyouts (e.g. PATH diagnostics) now render their own subtle acrylic surfaces layered over the window-level Mica Alt backdrop, tinted toward the system accent color. Metric cards and the Active Downloads strip use the shared translucent Mica-layer brushes — per-card acrylic over Mica Alt reads as flat gray, so acrylic is reserved for large/overlay surfaces. Shared brushes, the acrylic backdrop instance, and an acrylic `FlyoutPresenterStyle` live in a new `Themes/Backdrops.xaml` ResourceDictionary, with a `helpers:Backdrop.Acrylic` attached property for applying surfaces to any panel. When Windows transparency effects are disabled or a high-contrast theme is active, surfaces are skipped and opaque theme brushes keep text contrast AA compliant.
- **Fixed**: Home-page trend charts no longer render empty axes behind the "Sampling…" empty-state text — the chart only appears once data exists.
- **Live charts (LiveChartsCore.SkiaSharpView.WinUI 2.0.5, verified compatible with Windows App SDK 2.5.1)**:
  - **Downloads page**: a live per-transfer download-speed sparkline on each active transfer card and a total-throughput chart (all active transfers, MB/s) that collapses to an empty state when nothing is downloading.
  - **Installed page**: an accent-colored bar chart of CLI health-check probe latency (ms per tool) populated by "Check All CLIs", with an empty state before data exists.
  - **Home page**: session trend sparklines for active downloads and updates available.
  - All charts follow the system accent color and light/dark themes (repainted on `ActualThemeChanged`), sample at or below 4 updates/second (download pipeline 400 ms, page timers 250 ms–1 s), and release their timers, samples and series when navigating away (`OnNavigatedFrom`/`Unloaded`). Shared chart styling and bounded sample buffers live in `Helpers/ChartTelemetry.cs`.

### Changed
- **Tool card & catalog UX overhaul**:
  - **SplitButton install cards**: The catalog card's primary action is an accent `SplitButton` — Install — with a chevron menu offering "Choose version…", "Download only (skip install)", and "Open homepage". Stack cards on the Stacks page get the same treatment (Install stack, with "Customize in catalog" / "View downloads").
  - **Tool detail page with ConnectedAnimation**: Clicking a card's details (or context menu) now navigates to a new full-page `ToolDetailPage` — description, current version with known previous versions, vendor homepage, SHA-256 (copyable), favorite toggle and the same Install SplitButton. The card animates into the page via `ConnectedAnimationService` (forward direction).
  - **Segmented filters**: The category chip rows on Catalog and Stacks were replaced with the CommunityToolkit `Segmented` control (same filter behavior; favorites and multi-select unchanged).
  - **Skeleton loading**: Pulsing skeleton cards show while the catalog loads, instead of only a spinner.
  - **Reduce-motion respected**: All new motion (ConnectedAnimation, hover lift, pulse, skeletons) is suppressed when the Windows "reduce animations" accessibility setting is off (`Helpers/AnimationSettingsHelper`), showing final-state content instead.
  - The 90-tool grid keeps its `GridView` + `ItemsWrapGrid` panel, which already provides UI virtualization; converting to `ItemsView` would have risked the model-driven multi-select and favorites behavior for no virtualization gain.
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
