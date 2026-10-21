# Changelog

All notable changes to **DevOps Tools Installer** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Live version history in the version picker (`Services/ToolVersionHistoryService.cs`)**: the per-tool version picker (catalog card menu and tool detail pane) now fetches the **last 5 GitHub releases** of the vendor repository live and lists them below the curated catalog versions — newest first with publish date and a *pre-release* marker — so users who need an older build for compatibility can install exactly what the vendor shipped. For each release the service identifies the release asset matching Windows and the running architecture (ARM64-aware; falls back gracefully for platform-neutral Windows binaries like `kubectl.exe`), and picking one applies its **exact download URL and file name** via the new `ToolDefinition.SetVersionWithAsset` — vendors rename files between releases, so the templated file name alone would otherwise 404. Results are cached per repository for 10 minutes, failures are never cached and retry, non-GitHub-hosted tools keep using the curated `previousVersions` list, and the dialog stays fully usable while the fetch runs in the background.
- **Old versions for vendor-CDN tools**: tools whose download lives on the vendor's own site but whose binaries are mirrored to GitHub releases (*helm*, *kind*, *minikube*, *dbeaver*, extensible via `ToolVersionHistoryService.FallbackRepos`) also get the live last-5 list, with the vendor's preferred artifact (installer/setup) preferred when a repo ships several Windows binaries. Versioned CDNs that only advertise a `latest` channel (minikube/skaffold GCS mirrors) now rewrite `/releases/latest/` → `/releases/v{version}/` so their curated old versions download correctly.

### Website
- **New tool catalog page (`web/catalog.html`)**: the marketing site now mirrors the app's catalog — all **90 tools with their real vendor logos**, live versions, categories and artifact kind, filterable by category and searchable by name/description, plus the **12 curated stacks** rendered with the logos of every tool they contain. Cards deep-link (`catalog.html#terraform`) from the homepage and open the vendor's own homepage.
- **Homepage tool wall**: the landing page now shows the complete vendor logo wall (all 90 marks) straight under the hero, linking into the new catalog page — the app's catalog is finally visible on the site instead of only being described in prose.
- **Catalog → site export pipeline (`catalog/export-site-catalog.ps1`)**: `web/assets/catalog.json` and `web/assets/logos/` are **generated** from the signed catalog (`catalog/catalog.json` + `catalog/bundles.json`) and the app's logo assets, so the site can never drift from what the app installs. The Catalog Refresh Bot workflow now regenerates them on every run, and the export script is documented in `web/README.md`.
- **Product tour with the real app screenshots**: the homepage now shows **all 10 application screens** (Home, Tool Catalog, Stacks, Downloads, Installed, Profiles & History, Workstation Doctor, Custom Tools, Settings, About) in a floating wall — each capture in a bordered technical frame with a mono caption, drifting on a staggered 7.5 s float (disabled automatically for users who prefer reduced motion). The feature pages also swap their blueprint mockups for the genuine Stacks, Doctor and Downloads captures, and every image carries a descriptive `alt` plus intrinsic `width`/`height` to avoid layout shift.
- **Lighter site images (WebP + lazy loading)**: the 10 application screenshots are now served as **WebP with a PNG fallback** at two widths via `<picture>`/`srcset`, and every below-the-fold capture (product tour, feature pages, the 90-logo wall) is **lazy-loaded**. A phone now pulls the 960w variants — about **237 KB for the entire tour instead of 1.6 MB (‑85%)** — while desktop and 2× displays still get the full 1920w WebP. The reproducible pipeline lives in `web/tools/optimize-images.py` (`pip install pillow`), and `banner.png` is deliberately left as PNG because it is the `og:image` social preview.
- **Responsive hardening for phones and tablets**: grid children now opt out of the CSS Grid `min-width: auto` default, so long unbreakable content (winget package IDs, the file-location contract, tool names) can no longer push a column past the viewport on narrow screens — previously the feature cards overflowed by ~240 px and the download cards by ~44 px at 320–390 px widths. Inline code wraps (`overflow-wrap: anywhere`), stack-tool chips ellipsize their labels, and the catalog filter chips plus search field grew to 40–50 px touch targets on touch-sized viewports. Verified with a scripted probe across **320 / 360 / 390 / 768 / 1024 / 1280 px** on the homepage, catalog, download and features pages: zero horizontal overflow and zero overflowing elements on every combination.
- **Site UI polish**: new catalog components (`.tool-grid`, `.tool-card`, `.filter-chip`, `.search-field`, `.stack-card`, `.logo-wall`) added to the design system, a **Catalog** entry in every page's navigation and footer, the new page registered in `sitemap.xml`, and the download page's installation channels converted from a side-by-side card grid to a readable one-by-one list.

### Fixed
- **Remote catalog never loaded — wrong branch**: `CatalogService` fetched the fresh catalog from `raw.githubusercontent.com/…/main/…`, but the repository's default branch is `master`, so the request 404'd and every launch silently fell back to the embedded snapshot (the footer showed its age, e.g. *built-in snapshot (2026-09-23, 27 days old)*). Both remote URLs now use `/HEAD/`, which raw.githubusercontent resolves to the default branch regardless of renames.
- **Catalog refresh bot produced empty refreshes**: `refresh-catalog.ps1` crashed when bumping a tool with no `previousVersions` entry (`You cannot call a method on a null-valued expression`) and crashed again on the multi-element history list (`Unable to index into an object of type JsonObject` — PowerShell will not bind an `object[]` to the `JsonObject` indexer); both aborted whole refresh runs so bot PRs carried only signature churn. History is now read defensively and written as an explicit `JsonArray`.
- **Refresh bot skipped the most popular GitHub tools**: tools whose URL points at the `releases/latest/download/` channel (k9s, yq, jq, act, argocd, …) were never version-bumped because their URL doesn't embed the old version. The version and history are now bumped for them too — the artifact URL already serves the newest build, and the app's *Newer than catalog* detection and version-picker history stay accurate.
- **Old-version installs no longer silently download the latest release for fixed-link vendors**: `ToolDefinition.SupportsVersionTargeting` detects vendors that serve a single always-latest static URL (AWS CLI `AWSCLIV2.msi`, aka.ms redirects, Docker Desktop, Postman, VS Code, ngrok, …) where a pinned "older version" cannot change what gets downloaded. The version picker now shows a clear warning and disables version selection for those tools instead of silently installing the newest build while claiming to pin an old one.
- **App update check for Microsoft Store installs**: the previously installed-from-Store app queried GitHub Releases and, because packaged apps intentionally suppress the GitHub updater (`AppUpdaterService.CheckForUpdatesAsync` returns `null` for MSIX installs), the Settings and About pages misread that as *“Unable to reach GitHub Releases. Please check your internet connection.”* Both pages now branch on package identity: Store-managed installs query the **Microsoft Store** (`Windows.Services.Store` addendum — `Services/StoreUpdateService.cs`) instead of GitHub, surface pending Store package updates in the update banner (with the Store's own install prompt, falling back to opening the app's product page in the Store), and report *“Updates are managed by the Microsoft Store”* when up to date — so a Store install never sees a GitHub error. Portable and Inno Setup installs keep the GitHub Releases flow unchanged.
- **Store check threading**: `StoreContext` calls made on a desktop app's UI thread can deadlock (a documented Windows known issue), so every read-only Store query in `StoreUpdateService` (update listing, product-page lookup) now runs on a dedicated MTA thread with the result marshalled back, keeping only the window-initialized install prompt on the UI thread as the Store requires.
- **Downloads ETA was never visible**: the per-item ETA labels were bound to the `string` `DownloadEta` through the boolean `BoolToVisibilityConverter`, which always evaluated to false — the ETA was computed correctly but permanently hidden. `ToolDefinition` now exposes a real `HasEta` flag (with change notifications) that the labels bind to, so the *~42 s* estimate appears and clears with the transfer as intended.

## [3.1.0] - 2026-10-17

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
- **Shared page chrome (`Controls/PageHeader.xaml`, `Controls/PageContainer.cs`)**: every page now uses one header control (title, optional count badge, optional subtitle, optional right-aligned actions) inside one layout container (uniform 32/24 padding, 1200px centred content column so titles and left edges line up; `FullWidth="True"` for the Catalog and Installed grids so tables use every pixel).
- **Window material setting (`Settings → Appearance → Window material`)**: choose **Mica (neutral)**, **Mica Alt**, **Acrylic**, or **None** for the title bar and sidebar. Persisted in `settings.json`; applied live without a restart.
- **Home dashboard redesign (`Views/HomePage.xaml`)**:
  - Hero copy is capped at 55% of the banner and the artwork lives in its own column with a gradient-faded left edge, so the two can never overlap; a single **next best action** line (PATH warnings → *Run Doctor*, pending updates, active downloads) replaces the empty space under the calls to action.
  - **Workstation health ring** driven by `DoctorService.RunQuickHealthAsync()` — the fast Doctor subset (PATH, prerequisites, CLI shadowing, PATH integration) that skips the slow per-CLI version probes — with the score, pass/warn/fail counts and the failing checks listed inline.
  - **Recently installed** list (last 5 installs, updates and rollbacks) with the reason and date for each entry.
  - Four **equal-height, clickable** metric cards; *Needs attention* now states the reason on the card and deep-links to **Doctor**.
  - **Installs/updates by day** sparklines (last 30 days) rendered with LiveCharts, with a friendly empty state until there is history.
  - **Core features** is now a collapsed `Expander` placed directly under the metrics, so it is visible without scrolling.
- **Persisted local activity trend (`Services/InstallTrendService.cs`)**: aggregates installs and updates per day from the existing `history.jsonl` install history — a local file, never a network call — so the Home sparklines survive restarts and can never disagree with the History page.
- **Richer catalog (`Views/CatalogPage.xaml`)**: each tool card now carries a one-line description, the **vendor/source** derived from its homepage or download host, a favorite star, an **Update available** badge and an **Installed v…** indicator, and selection is a real per-card checkbox with an accent ring on the card itself.
- **Tool detail pane (`Views/CatalogPage.xaml`)**: clicking a card opens a right-hand pane (native `SplitView`) with the description, a version picker, a **Release notes** link, the **direct download link**, the **SHA-256** checksum (copyable), the artifact size once downloaded, the **license** (declared in the catalog, or resolved from the tool's GitHub repository), and the homepage — plus Install / Download only / full-details actions. It opens with a `ConnectedAnimation` from the card's logo, skipped under the Windows "reduce animations" setting.
- **Selection action bar (`Views/CatalogPage.xaml`)**: checking any card raises a floating bottom bar — *N selected*, **Download**, **Save as profile**, **Clear** — so batch actions no longer live in the toolbar.
- **Installed tools workspace (`Views/InstalledPage.xaml`)**: every row now shows **one primary action** — **Update** when a newer release is available, otherwise **Launch CLI** — and *Check updates*, *Pin version*, *Ignore this update*, *Open installation folder*, *Run CLI health check*, *Copy executable path*, *Open documentation* and *Uninstall* moved into a per-row **`…`** menu. Uninstall always asks for confirmation first.
  - Rows carry a checkbox, and checking any row raises a floating **Update selected** / **Uninstall selected** / **Clear** bar (with a select-all box in the table header), so bulk update and bulk removal no longer require visiting the catalog or acting one tool at a time.
  - The Status column shows a chip **only for exceptions** — *Update available*, *Not on PATH*, *Newer than catalog* — instead of stamping *Up to date* on every row. *Not on PATH* is computed by resolving each tool's executable and checking its folder against the User and Machine PATH, so a tool that is installed but unreachable from a fresh terminal is called out.
  - **Update preferences**: *Pin version* freezes a tool at the version on the machine (the version column then reads `(pinned)`), while *Ignore this update* dismisses only the exact release on offer so a later release is reported normally again. Both persist in `%LOCALAPPDATA%\DevOpsToolsInstaller\update-preferences.json` and are cleared once the update is applied. Update decisions are only honoured on this page: `HasUpdate` stays a pure catalog-versus-machine comparison everywhere else.
- **Installed tools list (`Views/InstalledPage.xaml`)**: an empty state for a machine with nothing deployed and a distinct one for a filter that matches nothing, both with a call to action.

### Changed
- `ArtifactService.Perform` now runs pre/post-install hooks (when enabled) and records successful installs to the install history.
- `UninstallService.Uninstall` now records successful uninstalls to the install history.
- `CatalogService` now merges user-defined custom tools into the catalog on load (`LoadCatalogAsync`).
- The window backdrop is no longer hard-coded to Mica Alt: it is driven by the saved **Window material** setting through `Helpers/SystemBackdropHelper.cs`, which resolves the requested material to one the machine can actually draw (Mica → Acrylic → solid) and forces an opaque surface when Windows "Transparency effects" is off or a high-contrast theme is active. The navigation pane's in-app `SystemBackdropElement` follows the same material, so the title bar and sidebar read as one surface against the opaque page content.

### UI & UX Improvements
- **Empty & loading states**: new reusable `EmptyStateControl` (`Controls/EmptyStateControl.xaml`) with icon, title, message and an optional call-to-action; applied to the Profiles, Snapshots, History and Custom Tools lists so no list renders blank when empty.
- **Keyboard shortcuts**: `Ctrl+F` focuses the global search, `Ctrl+,` opens Settings, `F5` refreshes the current page (via a new `IRefreshablePage` interface implemented across Home, Catalog, Stacks, Installed, Doctor, Downloads, Profiles and Custom Tools), and `Ctrl+1`–`Ctrl+7` jump between sections.
- **Command palette**: added *Refresh current page*, *Open downloads folder* and *Capture workstation snapshot* actions.
- **Responsive layouts**: `NavigationView` now uses an adaptive pane (`PaneDisplayMode="Auto"`); `CatalogEditorPage` and `ToolDetailPage` use `VisualStateManager` breakpoints so two-column layouts stack and the install action row wraps on narrow windows. The shared page header moves its actions onto their own row below 780px; the Downloads toolbar reflows and the Installed table scrolls horizontally rather than clipping columns when the window is narrow.
- **Catalog workspace**: the crowded button strip is replaced by a native `CommandBar` (Sort, Filter, Presets, **Density** — card grid or compact list — Select all, Profile) whose least important commands fold into the overflow menu as the window narrows; the tool grid is a virtualized `ItemsRepeater` with shimmer placeholders while the catalog loads (suppressed under reduce-animations) and a friendly empty state when filters match nothing.
- **Category filter row**: the clipped segmented tabs are gone. Categories are chips built into a horizontal `StackPanel` inside a `ScrollViewer`, so the row scrolls instead of clipping, with scroll buttons and theme-aware gradient edge fades that appear only on the side that has more to show.
- **Installed toolbar**: the page's loose controls are consolidated into a single stretched `CommandBar` — a segmented **All / Updates / Not on PATH** filter at the left and **Update all**, *Check updates*, *Check CLIs*, *Shell completion*, *Open Tools folder* and *Export* on the right, with the least important commands folding into the overflow menu as the window narrows. The redundant *Check for Updates* header button is gone because *Check updates* already performs the same re-scan and version re-check. The **Name**, **Status** and **Current Version** column headers are now buttons that sort the table (click the active column again to reverse it), and the live status line moved down to the footer, next to the catalog source and freshness summary.
- **Stacks workspace**: stack cards now size to their content instead of a fixed 292px cell — the wrap-grid cell height is measured from the tallest card, so there is no dead space and the install action sits at the bottom of every card. The icon-only preview strip became a short list of the included tools with their **names and versions** (plus a *+N more tools* line), each row carrying a *name • version • status* tooltip so identical icons stay distinguishable. Every card now shows an **X of Y installed** progress bar and the stack's estimated download size. The unlabelled library button beside *Install Stack* is now **View tools**, and clicking a card (or *View tools*) opens a right-hand detail pane listing every tool with its version, size and install state, a per-tool checkbox to choose exactly what is queued, **Select all**, a live *≈ size to download* readout and an **Install N selected** action.
- **Home page layout**: the hero drops the artwork below 880px so the copy keeps the full banner width, and the health/recently-installed and trend rows collapse from two columns to one below 1180px; the health card's actions sit on their own row so they never clip.
- **Tool detail upgrades**: new *Installation status* card (installed / downloaded / not installed with detected version), a *What's new in this version* release-notes expander (fetched from the vendor's GitHub releases when available), and a *Related tools* card linking to other tools in the same category.
- **Custom Tools workspace (`Views/CatalogEditorPage.xaml`)**: the form and *Your custom tools* list now share one two-column grid that meets in the middle — the form fills its column instead of leaving a dead gap before a far-right list — and a **live preview card** renders the entry exactly as it will appear in the catalog (logo or glyph, name, vendor, one-line description, category, kind and version). Fields now validate **inline** (id shape, HTTPS download URL, 64-hex SHA-256), the category is an **editable combo box** pre-filled with every category already in use, and the file name is **auto-suggested** from the URL and artifact kind (with a *use suggested name* link once you type your own). An optional **Compute SHA-256 from URL** action downloads the artifact to a temporary file and hashes it — clearly warning that this only proves the bytes are unchanged on later downloads, *not* that the file is authentic, and that the Authenticode signature check still runs at install time and is never skipped. Import/Export and the shared validation pipeline are unchanged.
- **Settings workspace (`Views/SettingsPage.xaml`)**: the page is rebuilt on the toolkit's `SettingsCard` / `SettingsExpander`. Every card now carries a **bold header and a description**, and each section shows its **current value on the right when collapsed** (e.g. *Theme: System default · Material: Mica*, *Policy: Warn*, *Proxy: Off*, *Hooks: Off*). A **search box** at the top filters sections and cards by title or description as you type — matching expanders open to reveal their results and their previous open/closed state is restored when the box is cleared, with a *no settings match* empty state and a *Clear search* action. A new **Security posture** summary sits at the top (signature policy, hooks on/off, proxy status and the audit-log location), and the unsigned-installer warning is now a compact `InfoBar` with a one-click **Switch to Block** action. Every section also gains a **Reset to defaults** action that restores only that section. Every existing binding, handler and persisted setting is unchanged.
- **Workstation Doctor workspace (`Views/DoctorPage.xaml`)**: the summary chips now stay **pinned at the top** while the results scroll, and checks are grouped into **collapsible `Expander`s ordered by severity** — groups with failures first, then warnings, then fully-passing groups (which start collapsed) — with each group showing a *Failed / Warning / Passed* badge and a check count, and its checks listed failed-first. The page now shows a **0-100 health score** chip (weighted so warnings count half and failures not at all) that is also fed to the **Home** health ring through `DoctorReport.HealthScore`. A new **Fix all safe issues** action previews the exact duplicate and dead PATH entries it will remove in a confirmation dialog, **backs up the User PATH first**, then offers an **Undo** that restores the pre-fix value. *Copy report* gains a **Redact my username and machine name** option (on by default) that rewrites `C:\Users\<name>` to `%USERPROFILE%` and the machine name to `%COMPUTERNAME%`, and an optional **Re-check on app start** toggle runs the fast health scan in the background and raises a toast only when it finds something to fix.
- **Visual polish**: subtle springy hover lift (`helpers:Motion.HoverLift`) on list-item cards across the Profiles and Custom Tools pages.
- **Accessibility & discoverability**: automation names on the empty-state control and its action button, plus tooltips on every navigation item describing the destination.
- **Profiles empty state & starter templates**: the Profiles toolbar now hides while the list is empty so the *Capture installed…* action is no longer shown twice; the empty state instead offers a single capture CTA plus three one-click **starter templates** (Kubernetes developer, Cloud engineer, IaC / Platform) that create a profile directly from the matching catalog tools, each card showing how many of its tools exist in the catalog.
- **Downloads telemetry & filters**: each downloading item now shows a live **ETA** (e.g. *~42 s*) alongside its transfer speed, derived from the instantaneous rate and remaining bytes; the status filter chips are replaced by a native **`Segmented`** control (All / Downloading / Scheduled / Ready to Install / Installed); and a **ProgressRing loading state** replaces the premature empty-state flash while the catalog hydrates and the queue is first populated.
- **Loading states everywhere data loads asynchronously**: Profiles, Installed and Stacks now show a **ProgressRing** overlay while their backing data hydrates (catalog load / workstation scan), so no page renders blank or flashes an empty state before its data is ready.
- **Accent SplitButtons**: introduced a unified `AccentSplitButtonStyle` and `CustomAccentDividerBrush` with consistent theme-aware hover and press states across the Catalog, Stacks, and Tool Details pages.
- **Far-right corner scrollbar alignment**: updated `PageContainer` and page layouts across all views so child scrollables span the full available width (`finalSize.Width`), pinning vertical scrollbars cleanly to the far-right corner with 32px breathing room for content.

### Fixed
- **Sidebar selection now follows the page**: `NavigationView.SelectedItem` is synced on `Frame.Navigated`, and an `ItemInvoked` safety net opens the target page even when the item was already selected. Fixes *Profiles & History* refusing to open and the wrong item being highlighted (e.g. *Installed* highlighting *Stacks*) when navigating from Home cards, the search box or deep links.
- **Crisp DirectWrite text rendering**: neutralized composition visual scale transforms on card hover and focus in `Helpers/Motion.cs` so text, glyphs, and badges remain 100% sharp and ClearType-aligned on both keyboard focus and mouse hover, eliminating bilinear interpolation blur.
- **Single version source of truth**: the title bar, the Home hero chip, the About badge and the Settings About row now all read `AppUpdaterService.CurrentVersion` (assembly version) instead of hard-coded, divergent values.
- **Consistent installer copy**: Home, About and the feature cards now describe the same behavior — vendor setup wizards run interactively with standard UAC prompts and are never installed silently.
- **Version state**: installed-vs-catalog comparison now uses a numeric comparer and distinguishes *Newer than catalog* from *Update available* / *Up to date* / *Installed*; the Installed and Catalog footers surface the active catalog source and how stale it is.
- **Custom Tools editor**: the SHA-256 label and placeholder agree, and the file-name placeholder now matches the selected artifact kind (installer, archive, binary or script).
- **Layout fixes**: the Installed row actions column is wide enough that the *Uninstall* button is no longer clipped, and the Home *Needs attention* (PATH status) metric card matches the height of the neighbouring metric cards.
- **Installed scan threading**: installed-tool detection and PATH resolution now run on a background thread but apply their results back on the UI thread, so rows are no longer handed property changes from a background thread while they are bound. The version-probe sweep that follows also releases its concurrency slot only when it actually acquired one, instead of over-releasing the limiter when a scan is cancelled by navigating away.
- **Backdrop**: the content host uses an opaque, theme-aware surface so the desktop wallpaper can no longer bleed through Mica Alt as a faint large shape behind page content.

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
