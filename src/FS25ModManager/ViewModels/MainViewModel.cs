using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FS25ModManager.Models;
using FS25ModManager.Services;
using Wpf.Ui.Controls;

namespace FS25ModManager.ViewModels;

public enum ModFilter { All, Active, Inactive, Issues, Unused }

public enum ModSort { Name, Size, Newest }

public enum AppPage { Mods, Savegames, About }

public partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, ImageSource> _iconCache = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _watchDebounce;
    private CancellationTokenSource? _iconLoading;
    private int _runningOperations;

    public MainViewModel()
    {
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMod;
        ApplySort();

        _watchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _watchDebounce.Tick += async (_, _) =>
        {
            _watchDebounce.Stop();
            if (_runningOperations > 0 || IsLoading)
            {
                _watchDebounce.Start(); // Try again once our own file operations are done.
                return;
            }
            await RefreshAsync();
        };
    }

    /// <summary>Shows a confirmation dialog: (title, message, confirmButtonText) → confirmed.</summary>
    public Func<string, string, string, Task<bool>> ConfirmAsync { get; set; } =
        (_, _, _) => Task.FromResult(true);

    public BulkObservableCollection<ModItem> Mods { get; } = [];

    public ICollectionView ModsView { get; }

    [ObservableProperty] private AppPage _currentPage = AppPage.Mods;

    [ObservableProperty] private string _modsDirectory = GamePaths.GetModsDirectory();
    [ObservableProperty] private string _backupDirectory = GamePaths.BackupDirectory;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ModFilter _filter = ModFilter.All;
    [ObservableProperty] private ModSort _sort = ModSort.Name;

    [ObservableProperty] private ModItem? _selectedMod;
    [ObservableProperty] private int _selectedCount;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _inactiveCount;
    [ObservableProperty] private int _issueCount;
    [ObservableProperty] private string _activeSize = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isDragOver;
    [ObservableProperty] private bool _isGameRunning;

    [ObservableProperty] private bool _isStatusOpen;
    [ObservableProperty] private string _statusTitle = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    public IReadOnlyList<ModSort> SortOptions { get; } = Enum.GetValues<ModSort>();

    public bool IsModsPage => CurrentPage == AppPage.Mods;
    public bool IsSavegamesPage => CurrentPage == AppPage.Savegames;
    public bool IsAboutPage => CurrentPage == AppPage.About;

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(IsModsPage));
        OnPropertyChanged(nameof(IsSavegamesPage));
        OnPropertyChanged(nameof(IsAboutPage));
    }

    partial void OnSearchTextChanged(string value) => ModsView.Refresh();
    partial void OnFilterChanged(ModFilter value) => ModsView.Refresh();
    partial void OnSortChanged(ModSort value) => ApplySort();

    private void ApplySort()
    {
        using (ModsView.DeferRefresh())
        {
            ModsView.SortDescriptions.Clear();
            switch (Sort)
            {
                case ModSort.Size:
                    ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItem.SizeBytes), ListSortDirection.Descending));
                    break;
                case ModSort.Newest:
                    ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItem.LastModified), ListSortDirection.Descending));
                    break;
            }
            ModsView.SortDescriptions.Add(new SortDescription(nameof(ModItem.Title), ListSortDirection.Ascending));
        }
    }

    private bool FilterMod(object obj)
    {
        if (obj is not ModItem mod) return false;

        bool matchesFilter = Filter switch
        {
            ModFilter.Active => mod.IsActive,
            ModFilter.Inactive => !mod.IsActive,
            ModFilter.Issues => mod.HasIssues,
            ModFilter.Unused => mod.IsUnused,
            _ => true,
        };
        if (!matchesFilter) return false;

        var query = SearchText.Trim();
        return query.Length == 0
            || mod.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || mod.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || mod.Author.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    // ── Loading ────────────────────────────────────────────────

    /// <summary>
    /// Rescans both folders and syncs the list: unchanged mods keep their row (and icon),
    /// removed ones disappear and new or changed ones are added.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            ModsDirectory = GamePaths.GetModsDirectory();
            BackupDirectory = GamePaths.BackupDirectory;
            IsGameRunning = GamePaths.IsGameRunning();

            var modsDir = ModsDirectory;
            var backupDir = BackupDirectory;
            var (items, savegames) = await Task.Run(() =>
            {
                Directory.CreateDirectory(backupDir);
                var list = ModScanner.ScanDirectory(modsDir, isActive: true);
                list.AddRange(ModScanner.ScanDirectory(backupDir, isActive: false));
                return (list, SavegameScanner.ScanAll(GamePaths.UserProfileDirectory));
            });
            ApplySavegames(savegames);

            foreach (var item in items)
            {
                if (_iconCache.TryGetValue(item.CacheKey, out var icon))
                    item.Icon = icon;
            }

            if (Mods.Count == 0)
            {
                Mods.ReplaceAll(items);
            }
            else
            {
                var incoming = items.ToDictionary(m => m.FullPath, StringComparer.OrdinalIgnoreCase);
                var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in Mods.ToList())
                {
                    if (incoming.TryGetValue(mod.FullPath, out var fresh)
                        && fresh.CacheKey == mod.CacheKey && fresh.IsActive == mod.IsActive)
                        kept.Add(mod.FullPath);
                    else
                        Mods.Remove(mod);
                }
                foreach (var item in items.Where(i => !kept.Contains(i.FullPath)))
                    Mods.Add(item);
            }

            UpdateAnalysis();
            ModsView.Refresh(); // The Issues filter depends on the analysis results.
            StartIconLoading();
            EnsureWatching();
        }
        catch (Exception ex)
        {
            ShowStatus("Could not load mods", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void StartIconLoading()
    {
        _iconLoading?.Cancel();
        var cts = new CancellationTokenSource();
        _iconLoading = cts;
        var pending = Mods.Where(m => m.Icon is null && m.IconEntry is not null && !m.IconRequested).ToList();
        foreach (var mod in pending) mod.IconRequested = true;
        var dispatcher = Application.Current.Dispatcher;

        _ = Task.Run(() =>
        {
            Parallel.ForEach(pending,
                new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cts.Token },
                mod =>
                {
                    var icon = IconLoader.Load(mod);
                    if (icon is null) return;
                    dispatcher.BeginInvoke(() =>
                    {
                        _iconCache[mod.CacheKey] = icon;
                        mod.Icon = icon;
                    });
                });
        }, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default); // Swallow cancellation.
    }

    private void UpdateAnalysis()
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inactive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods)
            (mod.IsActive ? active : inactive).Add(mod.ModName);

        foreach (var mod in Mods)
        {
            mod.HasDuplicate = mod.IsActive ? inactive.Contains(mod.ModName) : active.Contains(mod.ModName);

            var missing = mod.IsActive
                ? mod.Dependencies.Where(d => !active.Contains(d) && !d.StartsWith("pdlc_", StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            mod.MissingDependencies = missing.Count > 0 ? string.Join(", ", missing) : null;
        }

        TotalCount = Mods.Count;
        ActiveCount = Mods.Count(m => m.IsActive);
        InactiveCount = TotalCount - ActiveCount;
        IssueCount = Mods.Count(m => m.HasIssues);
        ActiveSize = Converters.BytesToStringConverter.Format(Mods.Where(m => m.IsActive).Sum(m => m.SizeBytes));

        UpdateSavegameAnalysis();
    }

    // ── Folder watching ────────────────────────────────────────

    private void EnsureWatching()
    {
        var wanted = new[] { ModsDirectory, BackupDirectory }.Where(Directory.Exists).ToList();
        if (_watchers.Count == wanted.Count
            && _watchers.All(w => wanted.Contains(w.Path, StringComparer.OrdinalIgnoreCase)))
            return;

        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();

        var dispatcher = Application.Current.Dispatcher;
        foreach (var dir in wanted)
        {
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler onChange = (_, _) => dispatcher.BeginInvoke(ScheduleWatchRefresh);
            watcher.Created += onChange;
            watcher.Deleted += onChange;
            watcher.Changed += onChange;
            watcher.Renamed += (_, _) => dispatcher.BeginInvoke(ScheduleWatchRefresh);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void ScheduleWatchRefresh()
    {
        _watchDebounce.Stop();
        _watchDebounce.Start();
    }

    // ── Enable / disable ───────────────────────────────────────

    [RelayCommand]
    private Task ToggleModAsync(ModItem? mod) =>
        mod is null ? Task.CompletedTask : SetActiveAsync([mod], !mod.IsActive);

    [RelayCommand]
    private Task EnableSelectedAsync(IList? selection) =>
        SetActiveAsync(selection?.OfType<ModItem>().ToList() ?? [], true);

    [RelayCommand]
    private Task DisableSelectedAsync(IList? selection) =>
        SetActiveAsync(selection?.OfType<ModItem>().ToList() ?? [], false);

    /// <summary>
    /// Activating moves a mod from the backup folder into the game's mods folder;
    /// deactivating moves it from the mods folder into the backup folder on the desktop.
    /// </summary>
    /// <param name="confirmed">The caller already asked the user; skip the running-game and dependency checks.</param>
    /// <param name="quiet">Only report failures; the caller shows its own summary.</param>
    /// <returns>Number of mods moved.</returns>
    private async Task<int> SetActiveAsync(IReadOnlyList<ModItem> requested, bool activate,
        bool confirmed = false, bool quiet = false)
    {
        var mods = requested.Where(m => m.IsActive != activate && !m.IsBusy).ToList();
        if (mods.Count == 0)
        {
            foreach (var m in requested) m.RevertActiveState();
            return 0;
        }

        if (!confirmed && !await ConfirmPreconditionsAsync(mods, activate))
        {
            foreach (var m in mods) m.RevertActiveState();
            return 0;
        }

        var destination = activate ? ModsDirectory : BackupDirectory;
        var failures = new List<string>();
        int moved = 0;
        _runningOperations++;

        try
        {
            foreach (var mod in mods)
            {
                mod.IsBusy = true;
                try
                {
                    string newPath;
                    try
                    {
                        newPath = await Task.Run(() => ModMover.Move(mod.FullPath, destination, replaceExisting: false));
                    }
                    catch (DestinationExistsException)
                    {
                        if (!await ConfirmReplaceAsync(mod.FileName, destination))
                        {
                            mod.RevertActiveState();
                            continue;
                        }
                        newPath = await Task.Run(() => ModMover.Move(mod.FullPath, destination, replaceExisting: true));
                        RemoveItemAt(Path.Combine(destination, mod.FileName), except: mod);
                    }

                    mod.FullPath = newPath;
                    mod.IsActive = activate;
                    moved++;
                }
                catch (Exception ex)
                {
                    mod.RevertActiveState();
                    failures.Add($"{mod.FileName}: {ex.Message}");
                }
                finally
                {
                    mod.IsBusy = false;
                }
            }
        }
        finally
        {
            _runningOperations--;
        }

        UpdateAnalysis();
        ModsView.Refresh();

        if (failures.Count > 0)
        {
            ShowFailures(failures);
        }
        else if (moved > 0 && !quiet)
        {
            var what = moved == 1 ? $"'{mods.First(m => m.IsActive == activate).Title}'" : $"{moved} mods";
            ShowStatus(activate ? "Enabled" : "Disabled",
                activate ? $"{what} moved to the game's mods folder." : $"{what} moved to the backup folder.",
                InfoBarSeverity.Success);
        }

        return moved;
    }

    private async Task<bool> ConfirmPreconditionsAsync(IReadOnlyList<ModItem> mods, bool activate)
    {
        if (!await ConfirmGameNotRunningAsync())
            return false;

        if (!activate)
        {
            var disabling = mods.Select(m => m.ModName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dependents = Mods
                .Where(m => m.IsActive && !disabling.Contains(m.ModName) && m.Dependencies.Any(disabling.Contains))
                .Select(m => m.Title)
                .ToList();
            if (dependents.Count > 0 && !await ConfirmAsync(
                    "Required by other mods",
                    "These active mods depend on what you are disabling:\n\n• " +
                    string.Join("\n• ", dependents.Take(10)) +
                    (dependents.Count > 10 ? $"\n…and {dependents.Count - 10} more" : "") +
                    "\n\nDisable anyway?",
                    "Disable"))
                return false;
        }

        return true;
    }

    private async Task<bool> ConfirmGameNotRunningAsync()
    {
        IsGameRunning = GamePaths.IsGameRunning();
        return !IsGameRunning || await ConfirmAsync(
            "Farming Simulator 25 is running",
            "Changes only take effect after restarting the game, and mods that are in use may fail to move.\n\nContinue anyway?",
            "Continue");
    }

    private Task<bool> ConfirmReplaceAsync(string fileName, string folder) =>
        ConfirmAsync(
            "Mod already exists",
            $"'{fileName}' already exists in:\n{folder}\n\n" +
            "Replace it? The existing copy will be moved to the Recycle Bin.",
            "Replace");

    private void RemoveItemAt(string path, ModItem? except = null)
    {
        var item = Mods.FirstOrDefault(m => m != except &&
            string.Equals(m.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (item is not null) Mods.Remove(item);
    }

    // ── Installing ─────────────────────────────────────────────

    /// <summary>Copies dropped/picked mods into the game's mods folder (they become active).</summary>
    public async Task InstallAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || IsInstalling) return;
        CurrentPage = AppPage.Mods;

        if (!await ConfirmGameNotRunningAsync())
            return;

        IsInstalling = true;
        _runningOperations++;
        var tempDir = Path.Combine(Path.GetTempPath(), "BGFS25ModManager", Guid.NewGuid().ToString("N"));
        var rejected = new List<string>();
        var installed = new List<string>();
        var renamed = new List<string>();
        var failures = new List<string>();

        try
        {
            var candidates = await Task.Run(() => ModInstaller.Prepare(paths, tempDir, rejected));
            var modsDir = ModsDirectory;

            foreach (var candidate in candidates)
            {
                try
                {
                    string Install(bool replace) => candidate.IsTemporary
                        ? ModMover.Move(candidate.SourcePath, modsDir, replace, candidate.TargetName)
                        : ModMover.Copy(candidate.SourcePath, modsDir, replace, candidate.TargetName);

                    try
                    {
                        await Task.Run(() => Install(false));
                    }
                    catch (DestinationExistsException)
                    {
                        if (!await ConfirmReplaceAsync(candidate.TargetName, modsDir))
                            continue;
                        await Task.Run(() => Install(true));
                        RemoveItemAt(Path.Combine(modsDir, candidate.TargetName));
                    }

                    installed.Add(candidate.TargetName);
                    if (candidate.OriginalName is not null)
                        renamed.Add($"{candidate.OriginalName} → {candidate.TargetName}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{candidate.TargetName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
        }
        finally
        {
            _runningOperations--;
            IsInstalling = false;
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* Temp folder cleanup is best effort. */ }
        }

        await RefreshAsync();

        if (installed.Count > 0)
            SelectedMod = Mods.FirstOrDefault(m => m.IsActive && m.FileName == installed[^1]);

        var problems = failures.Concat(rejected).ToList();
        if (installed.Count > 0)
        {
            var message = installed.Count == 1 ? $"'{installed[0]}' was added to the mods folder." : $"{installed.Count} mods were added to the mods folder.";
            if (renamed.Count > 0)
                message += "\nRenamed so the game can load them: " + string.Join(", ", renamed);
            if (problems.Count > 0)
                message += "\nSkipped: " + string.Join("; ", problems.Take(3));
            ShowStatus("Installed", message, problems.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        else if (problems.Count > 0)
        {
            ShowStatus("Nothing was installed", string.Join("\n", problems.Take(5)), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task BrowseAndInstallAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose mods to install",
            Filter = "Farming Simulator mods (*.zip)|*.zip",
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true)
            await InstallAsync(dialog.FileNames);
    }

    [RelayCommand]
    private async Task FixNameAsync(ModItem? mod)
    {
        if (mod is null || !mod.HasInvalidName) return;
        var newName = ModInstaller.ValidFileName(mod.FileName, mod.IsFolder);

        _runningOperations++;
        try
        {
            await Task.Run(() => ModMover.Rename(mod.FullPath, newName));
        }
        catch (Exception ex)
        {
            ShowStatus("Could not rename", ex.Message, InfoBarSeverity.Error);
            return;
        }
        finally
        {
            _runningOperations--;
        }

        await RefreshAsync();
        SelectedMod = Mods.FirstOrDefault(m => m.IsActive == mod.IsActive && m.FileName == newName);
        ShowStatus("Renamed", $"'{mod.FileName}' → '{newName}'. The game can load it now.", InfoBarSeverity.Success);
    }

    // ── Navigation & misc ──────────────────────────────────────

    [RelayCommand]
    private void ShowPage(AppPage page) => CurrentPage = page;

    [RelayCommand]
    private void OpenModsFolder() => OpenFolder(ModsDirectory);

    [RelayCommand]
    private void OpenBackupFolder() => OpenFolder(BackupDirectory);

    [RelayCommand]
    private void ShowInExplorer(ModItem? mod)
    {
        if (mod is null) return;
        Process.Start("explorer.exe", $"/select,\"{mod.FullPath}\"");
    }

    [RelayCommand]
    private static void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.Contains('@') && !url.Contains("://")) url = "mailto:" + url;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start("explorer.exe", $"\"{path}\"");
    }

    private void ShowFailures(List<string> failures) =>
        ShowStatus($"{failures.Count} mod(s) could not be moved",
            string.Join("\n", failures.Take(5)) + (failures.Count > 5 ? $"\n…and {failures.Count - 5} more" : ""),
            InfoBarSeverity.Error);

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
