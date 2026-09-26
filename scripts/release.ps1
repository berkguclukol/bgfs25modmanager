<#
.SYNOPSIS
  Builds, packs (Velopack) and optionally publishes a release to GitHub.

.EXAMPLE
  # 1. Bump <Version> in src/FS25ModManager/FS25ModManager.csproj and commit + push.
  # 2. Write the release notes to a markdown file.
  ./scripts/release.ps1 -Version 1.2.0 -NotesFile notes.md -Upload

  Without -Upload the packages are only built into releases/velopack for local testing.
  Installed apps update themselves from the GitHub release; the delta package keeps updates small.
#>
param(
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][string]$NotesFile,
  [switch]$Upload
)
$ErrorActionPreference = 'Stop'
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"

$repo = Split-Path $PSScriptRoot -Parent
$repoUrl = 'https://github.com/berkguclukol/bgfs25modmanager'
$project = Join-Path $repo 'src\FS25ModManager\FS25ModManager.csproj'
$outDir = Join-Path $repo 'releases\velopack'
$publishDir = Join-Path $repo "releases\publish-$Version"
$NotesFile = (Resolve-Path $NotesFile).Path

function Run([string]$what, [scriptblock]$cmd) {
  "== $what"
  & $cmd 2>&1 | ForEach-Object { "   $_" } | Select-Object -Last 6
  if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

if ((Get-Content $project -Raw) -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
  throw "<Version> in FS25ModManager.csproj is not $Version"
}
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
  Run 'install vpk' { dotnet tool install -g vpk }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force $outDir | Out-Null

Run 'publish' { dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publishDir }
Run 'download previous release (for delta updates)' { vpk download github --repoUrl $repoUrl -o $outDir }
Run 'pack' {
  vpk pack --packId BGFS25ModManager --packVersion $Version --packDir $publishDir --mainExe BGFS25ModManager.exe `
    --packTitle 'BG FS25 Mod Manager' --packAuthors 'Berk Güçlükol' `
    --icon (Join-Path $repo 'src\FS25ModManager\Assets\app.ico') `
    --splashImage (Join-Path $repo 'src\FS25ModManager\Assets\app.png') `
    --shortcuts 'Desktop,StartMenuRoot' --releaseNotes $NotesFile -o $outDir
}

if ($Upload) {
  $token = (gh auth token).Trim()
  Run 'upload to GitHub' {
    vpk upload github --repoUrl $repoUrl --token $token -o $outDir --publish --tag "v$Version" `
      --releaseName "BG FS25 Mod Manager v$Version"
  }
  # vpk uses the notes embedded in the package; set them explicitly so GitHub renders the markdown file as-is.
  Run 'set release notes' { gh release edit "v$Version" --repo $repoUrl --notes-file $NotesFile }
}

"== output ($outDir)"
Get-ChildItem $outDir | Where-Object { $_.Name -like "*$Version*" -or $_.Name -like '*-win-*' } |
  ForEach-Object { "   {0,-44} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
