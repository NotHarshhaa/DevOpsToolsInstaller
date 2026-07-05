# Contributing to DevOpsToolsInstaller

Thanks for helping make workstation setup less painful! Contributions of all
sizes are welcome — the most valuable ones are **catalog additions** and
**keeping download URLs and versions current**.

## Ways to contribute

- **Add a tool** to the catalog (see below).
- **Fix a stale download URL or version** in `catalog/catalog.json`.
- **Report or fix a bug** in the app.
- **Improve docs.**

## Adding or updating a catalog entry

The catalog lives in [`catalog/catalog.json`](catalog/catalog.json) — a flat
JSON array. Add or edit one object per tool:

```jsonc
{
  "id": "terraform",                 // unique, stable identifier (also the extract-folder name)
  "name": "Terraform",               // display name
  "category": "Infrastructure as Code",
  "description": "Infrastructure as Code tool for provisioning cloud resources",
  "iconGlyph": "\uE74C",             // Segoe Fluent Icons glyph
  "kind": "archive",                 // installer | archive | binary | script
  "version": "1.9.5",                // optional, shown in the UI
  "homepage": "https://www.terraform.io/",  // optional
  "downloadUrl": "https://releases.hashicorp.com/terraform/1.9.5/terraform_1.9.5_windows_amd64.zip",
  "fileName": "terraform_windows_amd64.zip",
  "sha256": "",                      // optional; verified after download when set
  "launchArgs": ""                   // optional args passed to an installer
}
```

### Guidelines

1. **Use the official vendor URL.** Point `downloadUrl` at the vendor's own
   release host or a first-party mirror. No repackaged or third-party binaries.
2. **Set the correct `kind`.** This decides the post-download action:
   - `installer` — `.exe`/`.msi` setups that install themselves
   - `archive` — `.zip` files that need extracting
   - `binary` — a single standalone `.exe` (a CLI)
   - `script` — `.ps1`/shell scripts the user should review first (never auto-run)
3. **Pick a unique, stable `id`** (lowercase, hyphenated). It's used for the
   per-tool extract folder, so changing it later orphans old folders.
4. **Match `fileName` to the artifact.** Include the correct extension — the
   app also uses it to infer `kind` when `kind` is omitted.
5. **Fill in `version` and `homepage` when you can**; add a `sha256` if the
   vendor publishes one.
6. **Keep entries alphabetical-ish within their category** for easy diffs.

## Building and testing locally

Requires the **.NET 8 SDK** on Windows 10/11.

```powershell
.\build.ps1 -Run          # build (Release, self-contained) and launch
```

Or with the SDK directly:

```powershell
dotnet build src/DevOpsToolsInstaller/DevOpsToolsInstaller.csproj -c Release
```

Before opening a PR:

- Confirm the project builds with no new warnings.
- If you changed the catalog, verify it's valid JSON and that your new
  tool appears, downloads, and takes the expected action.

## Pull requests

- Keep PRs focused; one logical change per PR.
- Use a concise title and describe what you changed and how you tested it.
- Link any related issue.

## Code of conduct

Be respectful and constructive. We're all here to make DevOps setup easier.
