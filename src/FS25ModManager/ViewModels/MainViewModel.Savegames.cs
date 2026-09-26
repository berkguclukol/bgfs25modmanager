using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FS25ModManager.Models;
using Wpf.Ui.Controls;

namespace FS25ModManager.ViewModels;

public partial class MainViewModel
{
    public BulkObservableCollection<Savegame> Savegames { get; } = [];

    [ObservableProperty] private Savegame? _selectedSavegame;
    [ObservableProperty] private int _unusedCount;
    [ObservableProperty] private string _unusedSize = string.Empty;

    private void ApplySavegames(List<Savegame> savegames)
    {
        var selectedSlot = SelectedSavegame?.Slot;
        Savegames.ReplaceAll(savegames);
        SelectedSavegame = Savegames.FirstOrDefault(s => s.Slot == selectedSlot)
            ?? Savegames.OrderByDescending(s => s.SaveDate).FirstOrDefault();
    }

    /// <summary>
    /// Works out, for every savegame, which mods it needs and whether they are installed, and marks
    /// mods that no savegame needs as unused.
    /// </summary>
    private void UpdateSavegameAnalysis()
    {
        // Prefer the active copy when the same mod is in both folders.
        var installed = new Dictionary<string, ModItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods.OrderBy(m => m.IsActive ? 0 : 1))
            installed.TryAdd(mod.ModName, mod);

        var usage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var save in Savegames)
        {
            var selected = save.SelectedMods
                .GroupBy(m => m.ModName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Needed: the map, anything with objects in the save, and selected mods that work without objects
            // (scripts, sounds, textures…)…
            var needed = new HashSet<string>(save.ModsWithObjects, StringComparer.OrdinalIgnoreCase);
            if (save.MapModName is not null) needed.Add(save.MapModName);
            foreach (var name in selected.Keys)
            {
                if (installed.TryGetValue(name, out var mod) && mod.WorksWithoutObjects)
                    needed.Add(name);
            }

            // …plus everything those depend on.
            var queue = new Queue<string>(needed);
            while (queue.Count > 0)
            {
                if (!installed.TryGetValue(queue.Dequeue(), out var mod)) continue;
                foreach (var dep in mod.Dependencies)
                {
                    if (!dep.StartsWith("pdlc_", StringComparison.OrdinalIgnoreCase) && needed.Add(dep))
                        queue.Enqueue(dep);
                }
            }

            SaveModRow Row(string name)
            {
                installed.TryGetValue(name, out var mod);
                selected.TryGetValue(name, out var reference);
                return new SaveModRow
                {
                    ModName = name,
                    Title = mod?.Title ?? (reference?.Title is { Length: > 0 } t ? t : name),
                    Version = reference?.Version ?? mod?.Version ?? string.Empty,
                    Status = mod is null ? SaveModStatus.NotInstalled : mod.IsActive ? SaveModStatus.Active : SaveModStatus.Disabled,
                    Mod = mod,
                    HasObjects = save.ModsWithObjects.Contains(name),
                    IsMap = string.Equals(name, save.MapModName, StringComparison.OrdinalIgnoreCase),
                    IsScript = mod?.HasScripts == true,
                    IsGlobal = mod?.WorksWithoutObjects == true,
                };
            }

            // Missing first, then disabled, then active; maps on top within each group.
            var neededRows = needed.Select(Row)
                .OrderByDescending(r => r.Status)
                .ThenByDescending(r => r.IsMap)
                .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var otherRows = selected.Keys.Where(n => !needed.Contains(n)).Select(Row)
                .OrderByDescending(r => r.Status)
                .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            save.NeededMods = neededRows;
            save.OtherMods = otherRows;
            save.NeededCount = neededRows.Count;
            save.DisabledNeededCount = neededRows.Count(r => r.Status == SaveModStatus.Disabled);
            save.MissingCount = neededRows.Count(r => r.Status == SaveModStatus.NotInstalled);

            var wanted = new HashSet<string>(needed, StringComparer.OrdinalIgnoreCase);
            wanted.UnionWith(selected.Keys);
            save.ToEnableCount = wanted.Count(n => installed.TryGetValue(n, out var m) && !m.IsActive);
            save.ToDisableCount = Mods.Count(m => m.IsActive && !wanted.Contains(m.ModName));

            foreach (var name in needed)
            {
                if (!usage.TryGetValue(name, out var list))
                    usage[name] = list = [];
                list.Add($"Slot {save.Slot} · {save.MapTitle}");
            }
        }

        foreach (var mod in Mods)
        {
            var used = usage.TryGetValue(mod.ModName, out var saves);
            mod.UsedInSaves = used ? string.Join("\n", saves!) : null;
            mod.IsUnused = Savegames.Count > 0 && !used;
        }

        var unused = Mods.Where(m => m.IsUnused).ToList();
        UnusedCount = unused.Count;
        UnusedSize = Converters.BytesToStringConverter.Format(unused.Sum(m => m.SizeBytes));
    }

    /// <summary>Moves the mods this savegame needs out of the backup folder.</summary>
    [RelayCommand]
    private async Task EnableNeededModsAsync(Savegame? save)
    {
        if (save is null) return;
        var mods = save.NeededMods
            .Where(r => r.Status == SaveModStatus.Disabled && r.Mod is not null)
            .Select(r => r.Mod!)
            .ToList();

        if (mods.Count == 0)
        {
            ShowStatus("Nothing to enable",
                save.MissingCount > 0
                    ? $"All installed mods for this save are active, but {save.MissingCount} needed mod(s) are not installed."
                    : "Everything this savegame needs is already active.",
                save.MissingCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
            return;
        }

        await SetActiveAsync(mods, activate: true);
    }

    /// <summary>Activates exactly the mods selected for this savegame and moves every other mod to the backup folder.</summary>
    [RelayCommand]
    private async Task UseOnlySaveModsAsync(Savegame? save)
    {
        if (save is null) return;

        var wanted = new HashSet<string>(save.NeededMods.Select(r => r.ModName), StringComparer.OrdinalIgnoreCase);
        wanted.UnionWith(save.OtherMods.Select(r => r.ModName));

        var toEnable = save.NeededMods.Concat(save.OtherMods)
            .Where(r => r.Status == SaveModStatus.Disabled && r.Mod is not null)
            .Select(r => r.Mod!)
            .ToList();
        var toDisable = Mods.Where(m => m.IsActive && !wanted.Contains(m.ModName)).ToList();

        if (toEnable.Count == 0 && toDisable.Count == 0)
        {
            ShowStatus("Already set up", $"The mods folder already matches {save.DisplayTitle}.", InfoBarSeverity.Informational);
            return;
        }

        if (!await ConfirmGameNotRunningAsync())
            return;

        var message = $"Set up the mods folder for {save.DisplayTitle}:\n\n";
        if (toEnable.Count > 0) message += $"• Enable {toEnable.Count} mod(s) from the backup folder\n";
        if (toDisable.Count > 0)
            message += $"• Move {toDisable.Count} mod(s) this save doesn't use to the backup folder " +
                       $"({Converters.BytesToStringConverter.Format(toDisable.Sum(m => m.SizeBytes))})\n";
        if (save.MissingCount > 0) message += $"\n{save.MissingCount} needed mod(s) are not installed and must be downloaded.\n";
        message += "\nYou can switch back at any time — nothing is deleted.";

        if (!await ConfirmAsync("Use only this save's mods", message, "Apply"))
            return;

        int disabled = toDisable.Count > 0 ? await SetActiveAsync(toDisable, activate: false, confirmed: true, quiet: true) : 0;
        int enabled = toEnable.Count > 0 ? await SetActiveAsync(toEnable, activate: true, confirmed: true, quiet: true) : 0;

        if (!IsStatusOpen || StatusSeverity != InfoBarSeverity.Error)
        {
            ShowStatus($"Ready for {save.DisplayTitle}",
                $"{enabled} mod(s) enabled, {disabled} mod(s) moved to the backup folder.",
                InfoBarSeverity.Success);
        }
    }

    /// <summary>Moves every mod that no savegame needs to the backup folder.</summary>
    [RelayCommand]
    private async Task DisableUnusedAsync()
    {
        var mods = Mods.Where(m => m.IsActive && m.IsUnused).ToList();
        if (mods.Count == 0)
        {
            ShowStatus("Nothing to disable", "Every active mod is used by at least one savegame.", InfoBarSeverity.Informational);
            return;
        }

        if (!await ConfirmAsync("Disable unused mods",
                $"Move {mods.Count} active mod(s) that no savegame uses to the backup folder " +
                $"({Converters.BytesToStringConverter.Format(mods.Sum(m => m.SizeBytes))})?\n\n" +
                "A mod counts as unused when no savegame has its vehicles, buildings or items, it isn't a savegame's map " +
                "and it isn't a script mod selected in a savegame. You can re-enable any of them at any time.",
                "Disable"))
            return;

        await SetActiveAsync(mods, activate: false, confirmed: true);
    }

    [RelayCommand]
    private void OpenSavegameFolder(Savegame? save)
    {
        if (save is not null)
            System.Diagnostics.Process.Start("explorer.exe", $"\"{save.FolderPath}\"");
    }

    [RelayCommand]
    private void ShowModInList(SaveModRow? row)
    {
        if (row?.Mod is null) return;
        SearchText = string.Empty;
        Filter = ModFilter.All;
        SelectedMod = row.Mod;
        CurrentPage = AppPage.Mods;
    }
}
