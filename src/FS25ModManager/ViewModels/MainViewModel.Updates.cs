using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FS25ModManager.Services;

namespace FS25ModManager.ViewModels;

public partial class MainViewModel
{
    private readonly UpdateService _updates = new();

    /// <summary>Updates only work in the installed / portable app, not in a development build.</summary>
    public bool CanUpdate => _updates.IsSupported;

    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private string? _updateReadyVersion;
    [ObservableProperty] private string _updateStatus = "Updates are checked automatically when the app starts.";

    public bool IsUpdateReady => UpdateReadyVersion is not null;

    partial void OnUpdateReadyVersionChanged(string? value) => OnPropertyChanged(nameof(IsUpdateReady));

    private Task _updateCheck = Task.CompletedTask;

    /// <summary>Called once at startup: checks in the background and downloads a new version without asking.</summary>
    public Task CheckForUpdatesOnStartupAsync() => _updateCheck = CheckForUpdatesCoreAsync(userInitiated: false);

    [RelayCommand]
    private Task CheckForUpdatesAsync() => _updateCheck = CheckForUpdatesCoreAsync(userInitiated: true);

    /// <summary>
    /// Waits for a running update download to finish (up to <paramref name="timeout"/>), so closing the app
    /// mid-download still installs the update instead of postponing it to the next start.
    /// </summary>
    public Task WaitForUpdateDownloadAsync(TimeSpan timeout) =>
        IsCheckingForUpdates ? Task.WhenAny(_updateCheck, Task.Delay(timeout)) : Task.CompletedTask;

    private async Task CheckForUpdatesCoreAsync(bool userInitiated)
    {
        if (IsCheckingForUpdates || IsUpdateReady) return;

        if (!CanUpdate)
        {
            UpdateStatus = "Updates are available in the installed or portable version of the app.";
            return;
        }

        IsCheckingForUpdates = true;
        UpdateProgress = 0;
        UpdateStatus = "Checking for updates…";
        try
        {
            var version = await _updates.CheckAndDownloadAsync(p =>
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    UpdateProgress = p;
                    UpdateStatus = $"Downloading update… {p}%";
                }));

            if (version is null)
            {
                UpdateStatus = $"You're up to date (v{AppInfo.Version}). Last checked {DateTime.Now:HH:mm}.";
            }
            else
            {
                UpdateReadyVersion = version;
                UpdateStatus = $"Version {version} is ready. Restart to update — or it installs automatically when you close the app.";
            }
        }
        catch (Exception ex)
        {
            // Offline or GitHub unreachable: stay quiet on startup, explain when the user asked.
            UpdateStatus = userInitiated
                ? $"Couldn't check for updates: {ex.Message}"
                : "Couldn't check for updates. Try again later.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task RestartToUpdateAsync()
    {
        if (!IsUpdateReady) return;
        if (_runningOperations > 0 || IsInstalling)
        {
            ShowStatus("Please wait", "Mods are still being moved. Try again when it's done.", Wpf.Ui.Controls.InfoBarSeverity.Warning);
            return;
        }
        if (!await ConfirmAsync("Restart to update",
                $"BG FS25 Mod Manager will close, install version {UpdateReadyVersion} and open again. It only takes a few seconds.",
                "Restart now"))
            return;

        _updates.ApplyAndRestart();
    }

    /// <summary>Called when the window closes: installs a downloaded update silently.</summary>
    public void ApplyPendingUpdateOnExit()
    {
        if (!IsUpdateReady) return;
        try
        {
            _updates.ApplyOnExit();
        }
        catch
        {
            // Never block closing the app; the update will be downloaded again next time.
        }
    }
}
