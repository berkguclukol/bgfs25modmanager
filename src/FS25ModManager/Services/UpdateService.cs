using Velopack;
using Velopack.Sources;

namespace FS25ModManager.Services;

/// <summary>Checks GitHub Releases for new versions and installs them silently (Velopack).</summary>
public sealed class UpdateService
{
    /// <summary>Set to a local folder or URL to test updates without publishing (e.g. a vpk output folder).</summary>
    private const string SourceOverrideVariable = "BGFS25_UPDATE_SOURCE";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        var overrideSource = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        _manager = string.IsNullOrWhiteSpace(overrideSource)
            ? new UpdateManager(new GithubSource(AppInfo.GitHubUrl, accessToken: null, prerelease: false))
            : new UpdateManager(overrideSource);
    }

    /// <summary>False when running from a development build (not installed by Setup or the portable zip).</summary>
    public bool IsSupported => _manager.IsInstalled;

    /// <summary>Version of the downloaded update waiting to be installed, if any.</summary>
    public string? ReadyVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Checks for a newer version and downloads it. Returns the new version, or null when up to date.</summary>
    public async Task<string?> CheckAndDownloadAsync(Action<int>? progress = null)
    {
        if (!IsSupported) return null;

        var update = await _manager.CheckForUpdatesAsync();
        if (update is null) return null;

        await _manager.DownloadUpdatesAsync(update, progress);
        _pending = update;
        return ReadyVersion;
    }

    /// <summary>Installs the downloaded update and restarts the app.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    /// <summary>Installs the downloaded update silently after the app exits (no restart).</summary>
    public void ApplyOnExit()
    {
        if (_pending is not null)
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
    }
}
