using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FS25ModManager.Models;

public partial class ModItem : ObservableObject
{
    /// <summary>File or folder name, e.g. "FS25_MyMod.zip" or "FS25_MyMod".</summary>
    public required string FileName { get; init; }

    public required bool IsFolder { get; init; }

    /// <summary>The name the game uses to identify the mod (file name without .zip).</summary>
    public string ModName => IsFolder ? FileName : Path.GetFileNameWithoutExtension(FileName);

    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool? MultiplayerSupported { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public IReadOnlyList<string> Dependencies { get; set; } = [];
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>Script mod (has extraSourceFiles). These leave no objects in a savegame.</summary>
    public bool HasScripts { get; set; }

    /// <summary>The mod contains a map.</summary>
    public bool IsMap { get; set; }

    /// <summary>The mod adds things to the shop (vehicles, placeables, objects).</summary>
    public bool HasStoreItems { get; set; }

    /// <summary>
    /// Works just by being loaded (scripts, sounds, textures…) rather than through objects placed in a savegame,
    /// so it is needed whenever a savegame selects it.
    /// </summary>
    public bool WorksWithoutObjects => HasScripts || (!HasStoreItems && !IsMap);

    /// <summary>Savegames that use this mod, e.g. "Slot 1 · Riverbend Springs", or null.</summary>
    [ObservableProperty]
    private string? _usedInSaves;

    /// <summary>Not needed by any savegame.</summary>
    [ObservableProperty]
    private bool _isUnused;

    /// <summary>Path of the icon inside the zip (or relative to the mod folder).</summary>
    public string? IconEntry { get; set; }

    /// <summary>Set when modDesc.xml could not be read.</summary>
    public string? Error { get; set; }

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ImageSource? _icon;

    /// <summary>Another copy with the same name exists in the other folder.</summary>
    [ObservableProperty]
    private bool _hasDuplicate;

    /// <summary>Comma-separated list of required mods that are not active, or null.</summary>
    [ObservableProperty]
    private string? _missingDependencies;

    public bool IconRequested { get; set; }

    /// <summary>The game refuses to load mods with spaces, brackets etc. in the name.</summary>
    public bool HasInvalidName => !Services.ModInstaller.IsValidModName(ModName);

    public bool HasIssues => Error is not null || HasDuplicate || MissingDependencies is not null || HasInvalidName;

    /// <summary>Key that identifies the same file across moves, used to cache icons.</summary>
    public string CacheKey => $"{FileName}|{SizeBytes}|{LastModified.Ticks}";

    /// <summary>Re-notifies IsActive so a toggle that was flipped in the UI snaps back to the real state.</summary>
    public void RevertActiveState() => OnPropertyChanged(nameof(IsActive));

    partial void OnHasDuplicateChanged(bool value) => OnPropertyChanged(nameof(HasIssues));

    partial void OnMissingDependenciesChanged(string? value) => OnPropertyChanged(nameof(HasIssues));

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { FileName };
            if (Version.Length > 0) parts.Add($"v{Version}");
            if (Author.Length > 0) parts.Add(Author);
            return string.Join("  ·  ", parts);
        }
    }
}
