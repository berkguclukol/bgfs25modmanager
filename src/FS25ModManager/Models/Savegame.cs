using CommunityToolkit.Mvvm.ComponentModel;

namespace FS25ModManager.Models;

/// <summary>A mod listed in a savegame's careerSavegame.xml.</summary>
public sealed record SaveModRef(string ModName, string Title, string Version);

public enum SaveModStatus { Active, Disabled, NotInstalled }

/// <summary>One mod row on the savegame details panel.</summary>
public sealed class SaveModRow
{
    public required string ModName { get; init; }
    public required string Title { get; init; }
    public required string Version { get; init; }
    public required SaveModStatus Status { get; init; }
    public ModItem? Mod { get; init; }

    /// <summary>The savegame contains vehicles/buildings/items from this mod.</summary>
    public bool HasObjects { get; init; }
    public bool IsMap { get; init; }
    public bool IsScript { get; init; }

    /// <summary>Works just by being loaded (scripts, sounds, textures…).</summary>
    public bool IsGlobal { get; init; }

    public string StatusText => Status switch
    {
        SaveModStatus.Active => "Active",
        SaveModStatus.Disabled => "In backup",
        _ => "Not installed",
    };

    public string Kind => IsMap ? "Map" : HasObjects ? "Has items" : IsScript ? "Script" : IsGlobal ? "Global" : "Selected";
}

public partial class Savegame : ObservableObject
{
    public required int Slot { get; init; }
    public required string FolderPath { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MapTitle { get; init; } = string.Empty;

    /// <summary>Mod that provides the map (null for base-game and DLC maps).</summary>
    public string? MapModName { get; init; }

    public DateTime? CreationDate { get; init; }
    public DateTime? SaveDate { get; init; }
    public long? Money { get; init; }
    public double PlayTimeMinutes { get; init; }

    /// <summary>Mods selected for this savegame (DLCs excluded).</summary>
    public IReadOnlyList<SaveModRef> SelectedMods { get; init; } = [];

    /// <summary>Mods whose vehicles, placeables or items exist in this savegame.</summary>
    public IReadOnlySet<string> ModsWithObjects { get; init; } = new HashSet<string>();

    public string? Error { get; init; }

    public string DisplayTitle => $"Slot {Slot} · {(MapTitle.Length > 0 ? MapTitle : "Unknown map")}";

    public string PlayTimeText
    {
        get
        {
            var t = TimeSpan.FromMinutes(PlayTimeMinutes);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";
        }
    }

    public string MoneyText => Money is { } m ? m.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "–";

    // ── Filled in by the analysis, which depends on the installed mods ──

    /// <summary>Mods the savegame really needs: map, mods with objects, script mods and their dependencies.</summary>
    [ObservableProperty] private IReadOnlyList<SaveModRow> _neededMods = [];

    /// <summary>Selected in the savegame but nothing from them is placed or owned.</summary>
    [ObservableProperty] private IReadOnlyList<SaveModRow> _otherMods = [];

    [ObservableProperty] private int _neededCount;
    [ObservableProperty] private int _disabledNeededCount;
    [ObservableProperty] private int _missingCount;
    [ObservableProperty] private int _toEnableCount;
    [ObservableProperty] private int _toDisableCount;

    /// <summary>Everything the savegame needs is installed and active.</summary>
    public bool IsReady => Error is null && DisabledNeededCount == 0 && MissingCount == 0;

    partial void OnDisabledNeededCountChanged(int value) => OnPropertyChanged(nameof(IsReady));
    partial void OnMissingCountChanged(int value) => OnPropertyChanged(nameof(IsReady));
}
