# BG FS25 Mod Manager

A mod manager for Farming Simulator 25, built with C# / WPF (.NET 10).

## Features

- **Enable / disable mods** – disabling moves a mod to `Desktop\fs25modbackup`, enabling moves it back into the game's mods folder. Nothing is deleted.
- **Install mods** – drag and drop `.zip` files (or use *Install mods*). Mod bundles are unpacked, mods wrapped in an extra folder are repacked, and invalid names like `FS25_Mod (1).zip` are fixed.
- **Savegame analysis** – shows which mods each savegame needs (map, mods with vehicles/buildings/items in the save, script mods, dependencies), which are in the backup folder and which are missing.
  - *Enable what this save needs* and *Use only this save's mods*.
  - Mods that no savegame uses are flagged as **Unused**.
- **Checks** – duplicates in both folders, missing dependencies, unreadable `modDesc.xml`, invalid file names.
- Reads `modDesc.xml` (localized titles, `$l10n_` texts) and DDS icons; honors `modsDirectoryOverride` in `gameSettings.xml`; auto-refreshes when the folders change.

## Build

```bash
dotnet build
```

## Portable exe

```bash
dotnet publish src/FS25ModManager/FS25ModManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

The result is a single `publish/BGFS25ModManager.exe` that runs without installing .NET.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/FS25ModManager/Services/` | Scanning mods and savegames, moving/installing files |
| `src/FS25ModManager/ViewModels/` | Application logic (MVVM) |
| `src/FS25ModManager/MainWindow.xaml` | UI |
| `src/FS25ModManager/AppInfo.cs` | Name, version and developer details for the About page |
| `src/FS25ModManager/Assets/` | Logo (SVG sources, PNG, ICO) |

The version shown in the app comes from `<Version>` in `FS25ModManager.csproj`.

---

© 2026 Berk Güçlükol · [guclukol.net](https://guclukol.net) · Not affiliated with GIANTS Software.
