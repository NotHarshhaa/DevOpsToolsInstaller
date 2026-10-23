using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DevOpsToolsInstaller.Views;

public sealed partial class EnvironmentPage : Page, IRefreshablePage
{
    private List<string> _currentPathEntries = new();
    private List<PathItem> _allPathItems = new();
    private List<EnvVariableItem> _allVariables = new();
    private List<WslDistroInfo> _wslDistros = new();

    public EnvironmentPage()
    {
        InitializeComponent();
        Loaded += EnvironmentPage_Loaded;
    }

    void IRefreshablePage.RefreshView() => _ = RefreshAsync();

    private async void EnvironmentPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        ShowNotice("Refreshed workstation environment status across all tabs.", InfoBarSeverity.Informational);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadWslSectionAsync(),
                LoadPathAndVariablesSectionAsync(),
                LoadGitSshSectionAsync(),
                LoadTerminalPowerShellSectionAsync());
        }
        catch (Exception ex)
        {
            ShowNotice($"Refresh error: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ── TAB 1: WSL & VIRTUALIZATION ─────────────────────────────────────────

    private async Task LoadWslSectionAsync()
    {
        var prereqs = await WslService.CheckPrerequisitesAsync();
        WslHealthSummaryText.Text = prereqs.Summary;
        WslEngineStatusText.Text = prereqs.IsWslInstalled ? "Installed (Ready)" : "Not Detected";
        VmpStatusText.Text = prereqs.IsVirtualMachinePlatformEnabled ? "Enabled" : "Disabled (Needs Fix)";
        WslDefaultVersionText.Text = prereqs.WslVersion ?? "WSL 2";
        WslRecommendationsText.Text = string.Join("   •   ", prereqs.Recommendations);

        _wslDistros = await WslService.ListDistrosAsync();
        DistrosContainer.Children.Clear();

        if (_wslDistros.Count == 0)
        {
            DistrosContainer.Children.Add(new TextBlock
            {
                Text = "No Linux distributions installed yet. Use 'wsl --install -d Ubuntu' in a terminal to install Ubuntu.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        else
        {
            foreach (var d in _wslDistros)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                    Padding = new Thickness(12, 10, 12, 10),
                    CornerRadius = new CornerRadius(6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 2 };
                var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                title.Children.Add(new TextBlock { Text = d.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });

                if (d.IsDefault)
                {
                    var badge = new Border
                    {
                        Padding = new Thickness(6, 1, 6, 1),
                        CornerRadius = new CornerRadius(3),
                        Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "Default Distro",
                        FontSize = 10,
                        Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
                    };
                    title.Children.Add(badge);
                }

                info.Children.Add(title);
                info.Children.Add(new TextBlock
                {
                    Text = $"State: {d.State}  •  WSL Version: {d.Version}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

                var termBtn = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children = { new FontIcon { Glyph = "\uE756", FontSize = 11 }, new TextBlock { Text = "Open Terminal", FontSize = 12 } }
                    },
                    Padding = new Thickness(10, 4, 10, 4)
                };
                termBtn.Click += (s, e) => WslService.LaunchDistroTerminal(d.Name);
                actions.Children.Add(termBtn);

                var actionBtn = new Button
                {
                    Content = new TextBlock { Text = "Check Tools", FontSize = 12 },
                    Padding = new Thickness(10, 4, 10, 4)
                };
                actionBtn.Click += async (s, e) => await ProbeToolsForDistroAsync(d.Name);
                actions.Children.Add(actionBtn);

                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);

                card.Child = grid;
                DistrosContainer.Children.Add(card);
            }
        }
    }

    private void FixWslPrereqs_Click(object sender, RoutedEventArgs e)
    {
        var script = WslService.GeneratePrerequisiteFixScript();
        var tempFile = Path.Combine(Path.GetTempPath(), "Fix-DevOpsWslPrerequisites.ps1");
        File.WriteAllText(tempFile, script);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                Verb = "runas", // UAC elevation prompt
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            ShowNotice("Launched elevated prerequisite installer for WSL2 and Virtual Machine Platform.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not launch elevated script: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private async void ProbeWslTools_Click(object sender, RoutedEventArgs e)
    {
        var targetDistro = _wslDistros.FirstOrDefault(d => d.IsDefault)?.Name
            ?? _wslDistros.FirstOrDefault()?.Name
            ?? "Ubuntu";

        await ProbeToolsForDistroAsync(targetDistro);
    }

    private async Task ProbeToolsForDistroAsync(string distroName)
    {
        WslToolsContainer.Children.Clear();
        WslToolsContainer.Children.Add(new TextBlock
        {
            Text = $"Probing CLI tools inside '{distroName}'...",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        });

        var tools = await WslService.CheckToolsInsideDistroAsync(distroName);
        WslToolsContainer.Children.Clear();

        if (tools.Count == 0)
        {
            WslToolsContainer.Children.Add(new TextBlock
            {
                Text = $"Could not execute commands inside '{distroName}'. Distro may be stopped or not initialized.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }

        foreach (var t in tools)
        {
            var row = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock { Text = t.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameBlock, 0);
            grid.Children.Add(nameBlock);

            var statusPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            var badgeText = t.IsInstalledInWsl ? $"Installed: {t.WslPath}" : "Not installed inside WSL";
            statusPanel.Children.Add(new TextBlock
            {
                Text = badgeText,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = t.IsInstalledInWsl
                    ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            if (!string.IsNullOrEmpty(t.WslVersion))
            {
                statusPanel.Children.Add(new TextBlock
                {
                    Text = $"v{t.WslVersion}",
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                });
            }

            Grid.SetColumn(statusPanel, 1);
            grid.Children.Add(statusPanel);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            if (!t.IsInstalledInWsl)
            {
                var installBtn = new Button { Padding = new Thickness(8, 4, 8, 4), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                installBtn.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE896", FontSize = 11 }, new TextBlock { Text = "Install in WSL", FontSize = 11 } }
                };
                installBtn.Click += (s, e) =>
                {
                    WslService.InstallToolInDistro(distroName, t.ToolId, interactive: true);
                    ShowNotice($"Launched interactive terminal installer for {t.Name} inside {distroName}.", InfoBarSeverity.Informational);
                };
                btnPanel.Children.Add(installBtn);
            }

            var copyBtn = new Button { Padding = new Thickness(8, 4, 8, 4) };
            copyBtn.Content = new TextBlock { Text = "Copy Cmd", FontSize = 11 };
            copyBtn.Click += (s, e) =>
            {
                var cmd = WslService.GetInstallCommandForDistro(distroName, t.ToolId);
                CopyToClipboard(cmd);
                ShowNotice($"Copied install command for {t.Name} inside {distroName} to clipboard.", InfoBarSeverity.Success);
            };
            btnPanel.Children.Add(copyBtn);

            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            row.Child = grid;
            WslToolsContainer.Children.Add(row);
        }
    }

    // ── TAB 2: PATH & ENVIRONMENT VARIABLES ─────────────────────────────────

    private async Task LoadPathAndVariablesSectionAsync()
    {
        await Task.Yield();
        _allPathItems = EnvironmentVariableService.GetPathEntries(EnvironmentVariableTarget.User);
        _currentPathEntries = _allPathItems.Select(i => i.Path).ToList();

        RenderPathItems(SearchPathInput?.Text);

        // DevOps Presets
        PresetsContainer.Children.Clear();
        foreach (var p in EnvironmentVariableService.Presets)
        {
            var pRow = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(5)
            };

            var pGrid = new Grid();
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pName = new TextBlock { Text = p.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pName, 0);
            pGrid.Children.Add(pName);

            var pDesc = new TextBlock
            {
                Text = $"{p.Value} — {p.Description}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(pDesc, 1);
            pGrid.Children.Add(pDesc);

            var applyBtn = new Button { Content = new TextBlock { Text = "Apply Preset", FontSize = 11 }, Padding = new Thickness(8, 4, 8, 4) };
            applyBtn.Click += (s, e) =>
            {
                EnvironmentVariableService.ApplyPreset(p);
                ShowNotice($"Applied environment variable '{p.Name}={p.Value}' and broadcasted live update.", InfoBarSeverity.Success);
                _ = LoadPathAndVariablesSectionAsync();
            };
            Grid.SetColumn(applyBtn, 2);
            pGrid.Children.Add(applyBtn);

            pRow.Child = pGrid;
            PresetsContainer.Children.Add(pRow);
        }

        // Custom Variables
        _allVariables = EnvironmentVariableService.GetVariables(EnvironmentVariableTarget.User);
        RenderVariables(SearchVarInput?.Text);
    }

    private void RenderPathItems(string? filter)
    {
        PathItemsContainer.Children.Clear();
        var items = string.IsNullOrWhiteSpace(filter)
            ? _allPathItems
            : _allPathItems.Where(p => p.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (items.Count == 0)
        {
            PathItemsContainer.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(filter) ? "No PATH entries found." : "No matching PATH entries found.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idxText = new TextBlock
            {
                Text = $"#{item.Index + 1}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(idxText, 0);
            grid.Children.Add(idxText);

            var pathInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            pathInfo.Children.Add(new TextBlock
            {
                Text = item.Path,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            pathInfo.Children.Add(new TextBlock
            {
                Text = item.StatusText,
                FontSize = 10,
                Foreground = !item.Exists
                    ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                    : (item.IsDevOpsBin
                        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                        : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"])
            });

            Grid.SetColumn(pathInfo, 1);
            grid.Children.Add(pathInfo);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            int originalIdx = item.Index;

            var upBtn = new Button { Padding = new Thickness(6, 4, 6, 4), IsEnabled = originalIdx > 0 };
            upBtn.Content = new FontIcon { Glyph = "\uE70E", FontSize = 10 };
            upBtn.Click += (s, e) => MovePathEntry(originalIdx, -1);
            actions.Children.Add(upBtn);

            var downBtn = new Button { Padding = new Thickness(6, 4, 6, 4), IsEnabled = originalIdx < _currentPathEntries.Count - 1 };
            downBtn.Content = new FontIcon { Glyph = "\uE70D", FontSize = 10 };
            downBtn.Click += (s, e) => MovePathEntry(originalIdx, 1);
            actions.Children.Add(downBtn);

            var delBtn = new Button { Padding = new Thickness(6, 4, 6, 4) };
            delBtn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 };
            delBtn.Click += (s, e) => RemovePathEntry(originalIdx);
            actions.Children.Add(delBtn);

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            PathItemsContainer.Children.Add(card);
        }
    }

    private void RenderVariables(string? filter)
    {
        VariablesContainer.Children.Clear();
        var vars = string.IsNullOrWhiteSpace(filter)
            ? _allVariables
            : _allVariables.Where(v => v.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || v.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (vars.Count == 0)
        {
            VariablesContainer.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(filter) ? "No custom variables found." : "No matching variables found.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }

        foreach (var v in vars)
        {
            var vRow = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(5)
            };

            var vGrid = new Grid();
            vGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            vGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            vGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var vName = new TextBlock { Text = v.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(vName, 0);
            vGrid.Children.Add(vName);

            var vVal = new TextBlock { Text = v.Value, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(vVal, 1);
            vGrid.Children.Add(vVal);

            var delVarBtn = new Button { Padding = new Thickness(6, 4, 6, 4) };
            delVarBtn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 10 };
            delVarBtn.Click += (s, e) =>
            {
                EnvironmentVariableService.SetVariable(v.Name, null, EnvironmentVariableTarget.User);
                ShowNotice($"Removed variable '{v.Name}'.", InfoBarSeverity.Informational);
                _ = LoadPathAndVariablesSectionAsync();
            };
            Grid.SetColumn(delVarBtn, 2);
            vGrid.Children.Add(delVarBtn);

            vRow.Child = vGrid;
            VariablesContainer.Children.Add(vRow);
        }
    }

    private void SearchPathInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderPathItems(SearchPathInput.Text);
    }

    private void SearchVarInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderVariables(SearchVarInput.Text);
    }

    private void RestorePathBackup_Click(object sender, RoutedEventArgs e)
    {
        var (success, msg) = EnvironmentVariableService.RestoreLastPathBackup(EnvironmentVariableTarget.User);
        ShowNotice(msg, success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        if (success)
        {
            _ = LoadPathAndVariablesSectionAsync();
        }
    }

    private void MovePathEntry(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= _currentPathEntries.Count) return;

        var item = _currentPathEntries[index];
        _currentPathEntries.RemoveAt(index);
        _currentPathEntries.Insert(target, item);

        EnvironmentVariableService.SavePathEntries(_currentPathEntries, EnvironmentVariableTarget.User);
        _ = LoadPathAndVariablesSectionAsync();
    }

    private void RemovePathEntry(int index)
    {
        if (index < 0 || index >= _currentPathEntries.Count) return;
        var item = _currentPathEntries[index];
        _currentPathEntries.RemoveAt(index);

        EnvironmentVariableService.SavePathEntries(_currentPathEntries, EnvironmentVariableTarget.User);
        ShowNotice($"Removed '{item}' from PATH.", InfoBarSeverity.Informational);
        _ = LoadPathAndVariablesSectionAsync();
    }

    private void CleanPath_Click(object sender, RoutedEventArgs e)
    {
        var (dups, dead) = EnvironmentVariableService.CleanAndDeduplicatePath(EnvironmentVariableTarget.User);
        ShowNotice($"PATH cleaned: removed {dups} duplicate entries and {dead} non-existent folders. Backup created.", InfoBarSeverity.Success);
        _ = LoadPathAndVariablesSectionAsync();
    }

    private void EnsureBinOnPath_Click(object sender, RoutedEventArgs e)
    {
        EnvironmentVariableService.EnsureDevOpsBinOnPath();
        ShowNotice("Pinned '%LOCALAPPDATA%\\DevOpsToolsInstaller\\Tools\\bin' to the very top of User PATH.", InfoBarSeverity.Success);
        _ = LoadPathAndVariablesSectionAsync();
    }

    private async void BrowsePathFolder_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                NewPathInput.Text = folder.Path;
            }
        }
        catch { }
    }

    private void AddPathEntry_Click(object sender, RoutedEventArgs e)
    {
        var input = NewPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        var entries = _currentPathEntries;
        if (!entries.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(input);
            EnvironmentVariableService.SavePathEntries(entries, EnvironmentVariableTarget.User);
            NewPathInput.Text = "";
            ShowNotice($"Added '{input}' to User PATH.", InfoBarSeverity.Success);
            _ = LoadPathAndVariablesSectionAsync();
        }
    }

    private void AddVariable_Click(object sender, RoutedEventArgs e)
    {
        var name = NewVarNameInput.Text.Trim();
        var val = NewVarValueInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        EnvironmentVariableService.SetVariable(name, val, EnvironmentVariableTarget.User);
        NewVarNameInput.Text = "";
        NewVarValueInput.Text = "";
        ShowNotice($"Set environment variable '{name}={val}'.", InfoBarSeverity.Success);
        _ = LoadPathAndVariablesSectionAsync();
    }

    // ── TAB 3: GIT & SSH WIZARD ─────────────────────────────────────────────

    private async Task LoadGitSshSectionAsync()
    {
        var cfg = await GitSshSetupService.GetGitConfigAsync();
        GitNameInput.Text = cfg.UserName ?? "";
        GitEmailInput.Text = cfg.UserEmail ?? "";
        GitBranchInput.Text = cfg.DefaultBranch ?? "main";
        GitEditorInput.Text = cfg.Editor ?? "code --wait";

        if (cfg.AutoCrlf == "true") GitAutoCrlfCombo.SelectedIndex = 1;
        else if (cfg.AutoCrlf == "false") GitAutoCrlfCombo.SelectedIndex = 2;
        else GitAutoCrlfCombo.SelectedIndex = 0;

        if (string.Equals(cfg.CredentialHelper, "store", StringComparison.OrdinalIgnoreCase)) GitCredCombo.SelectedIndex = 1;
        else if (string.Equals(cfg.CredentialHelper, "cache", StringComparison.OrdinalIgnoreCase)) GitCredCombo.SelectedIndex = 2;
        else GitCredCombo.SelectedIndex = 0;

        // Existing SSH keys
        var keys = GitSshSetupService.GetExistingSshKeys();
        SshKeysContainer.Children.Clear();
        if (keys.Count == 0)
        {
            SshKeysContainer.Children.Add(new TextBlock
            {
                Text = "No SSH key pairs found in ~/.ssh. Use the generator below to create modern Ed25519 keys.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        else
        {
            foreach (var k in keys)
            {
                var kRow = new Border
                {
                    Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                    Padding = new Thickness(12, 8, 12, 8),
                    CornerRadius = new CornerRadius(5)
                };

                var kGrid = new Grid();
                kGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                kGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var kInfo = new StackPanel { Spacing = 2 };
                kInfo.Children.Add(new TextBlock { Text = k.DisplayTitle, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
                kInfo.Children.Add(new TextBlock
                {
                    Text = $"Fingerprint: {k.FingerprintShort}  •  Comment: {k.Comment ?? "—"}",
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                Grid.SetColumn(kInfo, 0);
                kGrid.Children.Add(kInfo);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

                var addAgentBtn = new Button { Padding = new Thickness(8, 4, 8, 4) };
                addAgentBtn.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE71B", FontSize = 10 }, new TextBlock { Text = "Add to Agent", FontSize = 11 } }
                };
                addAgentBtn.Click += async (s, e) =>
                {
                    var (success, msg) = await GitSshSetupService.AddKeyToSshAgentAsync(k.PrivateKeyPath);
                    ShowNotice(msg, success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
                };
                actions.Children.Add(addAgentBtn);

                var copyBtn = new Button { Padding = new Thickness(10, 4, 10, 4) };
                copyBtn.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { new FontIcon { Glyph = "\uE8C8", FontSize = 11 }, new TextBlock { Text = "Copy Public Key", FontSize = 11 } }
                };
                copyBtn.Click += (s, e) =>
                {
                    var pub = GitSshSetupService.ReadPublicKey(k.PublicKeyPath);
                    if (!string.IsNullOrEmpty(pub))
                    {
                        CopyToClipboard(pub);
                        ShowNotice($"Copied public key '{k.Name}.pub' to clipboard. Paste into GitHub (Settings > SSH Keys) or GitLab.", InfoBarSeverity.Success);
                    }
                };
                actions.Children.Add(copyBtn);

                Grid.SetColumn(actions, 1);
                kGrid.Children.Add(actions);

                kRow.Child = kGrid;
                SshKeysContainer.Children.Add(kRow);
            }
        }

        // OpenSSH Agent check
        bool agentRunning = await GitSshSetupService.IsSshAgentRunningAsync();
        SshAgentStatusText.Text = agentRunning
            ? "Status: Running and ready to manage keys."
            : "Status: Stopped or Disabled. Click below to enable automatic startup.";
        EnableSshAgentButton.IsEnabled = !agentRunning;
    }

    private async void SaveGitConfig_Click(object sender, RoutedEventArgs e)
    {
        var autocrlf = (GitAutoCrlfCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "input";
        var helper = (GitCredCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "manager";

        var cfg = new GitConfigSettings(
            GitNameInput.Text.Trim(),
            GitEmailInput.Text.Trim(),
            GitBranchInput.Text.Trim(),
            autocrlf,
            "false",
            helper,
            GitEditorInput.Text.Trim(),
            false);

        var success = await GitSshSetupService.SaveGitConfigAsync(cfg);
        if (success)
        {
            ShowNotice("Global Git configuration saved successfully.", InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice("Failed to update Git configuration.", InfoBarSeverity.Error);
        }
    }

    private async void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        var algo = (KeyAlgoCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ed25519";
        var email = KeyEmailInput.Text.Trim();
        var pass = KeyPassphraseInput.Password;

        ShowNotice($"Generating {algo.ToUpperInvariant()} key pair...", InfoBarSeverity.Informational);
        var res = await GitSshSetupService.GenerateSshKeyAsync(algo, email, null, pass);
        if (res.Success)
        {
            CopyToClipboard(res.PublicKeyContent);
            ShowNotice($"{res.Message} Public key copied to clipboard!", InfoBarSeverity.Success);
            await LoadGitSshSectionAsync();
        }
        else
        {
            ShowNotice(res.Message, InfoBarSeverity.Error);
        }
    }

    private void EnableSshAgent_Click(object sender, RoutedEventArgs e)
    {
        var script = GitSshSetupService.GetEnableSshAgentScript();
        var tempFile = Path.Combine(Path.GetTempPath(), "Enable-DevOpsSshAgent.ps1");
        File.WriteAllText(tempFile, script);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                Verb = "runas",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            ShowNotice("Launched elevated script to enable Windows ssh-agent service.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not launch script: {ex.Message}", InfoBarSeverity.Warning);
        }
    }

    private async void TestGitHubSsh_Click(object sender, RoutedEventArgs e)
    {
        ShowNotice("Testing SSH authentication to GitHub (git@github.com)...", InfoBarSeverity.Informational);
        var (success, output) = await GitSshSetupService.TestSshConnectionAsync("github.com");
        ShowNotice(success ? $"GitHub SSH Authenticated! {output}" : $"GitHub SSH Result: {output}", success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void TestGitLabSsh_Click(object sender, RoutedEventArgs e)
    {
        ShowNotice("Testing SSH authentication to GitLab (git@gitlab.com)...", InfoBarSeverity.Informational);
        var (success, output) = await GitSshSetupService.TestSshConnectionAsync("gitlab.com");
        ShowNotice(success ? $"GitLab SSH Authenticated! {output}" : $"GitLab SSH Result: {output}", success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    // ── TAB 4: TERMINAL & POWERSHELL ────────────────────────────────────────

    private async Task LoadTerminalPowerShellSectionAsync()
    {
        var term = await TerminalPowerShellSetupService.GetTerminalSettingsAsync();
        TerminalProfileCombo.Items.Clear();
        foreach (var p in term.Profiles)
        {
            var item = new ComboBoxItem { Content = p.Name, Tag = p.Guid ?? p.Name };
            TerminalProfileCombo.Items.Add(item);
            if (p.IsDefault) TerminalProfileCombo.SelectedItem = item;
        }

        TerminalFontCombo.Items.Clear();
        var fonts = new[] { "Cascadia Code", "Cascadia Code NF", "JetBrains Mono", "Fira Code", "MesloLGS NF", "Consolas" };
        foreach (var f in fonts)
        {
            var item = new ComboBoxItem { Content = f, Tag = f };
            TerminalFontCombo.Items.Add(item);
            if (string.Equals(term.CurrentFont, f, StringComparison.OrdinalIgnoreCase))
            {
                TerminalFontCombo.SelectedItem = item;
            }
        }
        if (TerminalFontCombo.SelectedItem is null && TerminalFontCombo.Items.Count > 0)
        {
            TerminalFontCombo.SelectedIndex = 0;
        }

        TerminalSchemeCombo.Items.Clear();
        foreach (var s in term.AvailableColorSchemes)
        {
            var item = new ComboBoxItem { Content = s, Tag = s };
            TerminalSchemeCombo.Items.Add(item);
            if (string.Equals(term.CurrentScheme, s, StringComparison.OrdinalIgnoreCase))
            {
                TerminalSchemeCombo.SelectedItem = item;
            }
        }
        if (TerminalSchemeCombo.SelectedItem is null && TerminalSchemeCombo.Items.Count > 0)
        {
            TerminalSchemeCombo.SelectedIndex = 0;
        }

        var pwsh = await TerminalPowerShellSetupService.GetPowerShellProfileStatusAsync();
        PwshProfilePathText.Text = $"Profile Path: {pwsh.ProfilePath} ({(pwsh.Exists ? "Active" : "Not yet created")})";
        OptCompletions.IsChecked = pwsh.HasDevOpsCompletions;
        OptAliases.IsChecked = pwsh.HasDevOpsAliases;
        OptStarship.IsChecked = pwsh.HasStarship;
    }

    private async void SaveTerminalSettings_Click(object sender, RoutedEventArgs e)
    {
        bool success = true;
        if (TerminalProfileCombo.SelectedItem is ComboBoxItem pItem && pItem.Tag is string pGuid)
        {
            success &= await TerminalPowerShellSetupService.SetTerminalDefaultProfileAsync(pGuid);
        }

        if (TerminalFontCombo.SelectedItem is ComboBoxItem fItem && fItem.Tag is string fFace)
        {
            success &= await TerminalPowerShellSetupService.SetTerminalFontFaceAsync(fFace);
        }

        if (TerminalSchemeCombo.SelectedItem is ComboBoxItem sItem && sItem.Tag is string sName)
        {
            success &= await TerminalPowerShellSetupService.SetTerminalColorSchemeAsync(sName);
        }

        if (success)
        {
            ShowNotice("Windows Terminal configuration applied successfully.", InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice("Could not write Windows Terminal settings.json (is Terminal installed?).", InfoBarSeverity.Warning);
        }
    }

    private async void ApplyPowerShellSetup_Click(object sender, RoutedEventArgs e)
    {
        bool completions = OptCompletions.IsChecked == true;
        bool aliases = OptAliases.IsChecked == true;
        bool starship = OptStarship.IsChecked == true;

        var success = await TerminalPowerShellSetupService.ApplyPowerShellSetupAsync(completions, aliases, starship);
        if (success)
        {
            ShowNotice("PowerShell profile updated with DevOps completions, aliases, and prompt hooks. Backup created as .bak.", InfoBarSeverity.Success);
            await LoadTerminalPowerShellSectionAsync();
        }
        else
        {
            ShowNotice("Failed to update PowerShell profile.", InfoBarSeverity.Error);
        }
    }

    private void OpenProfileInEditor_Click(object sender, RoutedEventArgs e)
    {
        bool opened = TerminalPowerShellSetupService.OpenProfileInEditor();
        if (opened)
        {
            ShowNotice("Opened PowerShell profile in system editor.", InfoBarSeverity.Informational);
        }
        else
        {
            ShowNotice("Could not open PowerShell profile.", InfoBarSeverity.Warning);
        }
    }

    // ── HELPERS ─────────────────────────────────────────────────────────────

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        PageNotice.Message = message;
        PageNotice.Severity = severity;
        PageNotice.IsOpen = true;
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }
        catch { }
    }
}
