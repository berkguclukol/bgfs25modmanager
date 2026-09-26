using System.Windows;
using System.Windows.Controls;
using FS25ModManager.ViewModels;
using Wpf.Ui.Controls;

namespace FS25ModManager;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private bool _finishingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.ConfirmAsync = ConfirmAsync;
        _viewModel.PropertyChanged += (_, e) =>
        {
            // Bring the selected mod into view, e.g. after jumping here from the Savegames page.
            if (e.PropertyName == nameof(MainViewModel.SelectedMod) && _viewModel.SelectedMod is { } mod)
                Dispatcher.BeginInvoke(() => ModList.ScrollIntoView(mod), System.Windows.Threading.DispatcherPriority.Background);
        };
        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            await _viewModel.CheckForUpdatesOnStartupAsync();
        };
        Closing += async (_, e) =>
        {
            // Closed while an update is still downloading: hide the window, finish the download
            // in the background, then close for real so the update installs silently.
            if (_finishingUpdate || !_viewModel.IsCheckingForUpdates) return;
            e.Cancel = true;
            _finishingUpdate = true;
            Hide();
            await _viewModel.WaitForUpdateDownloadAsync(TimeSpan.FromMinutes(2));
            Close();
        };
        Closed += (_, _) => _viewModel.ApplyPendingUpdateOnExit();
    }

    private static async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                MaxWidth = 460,
            },
            PrimaryButtonText = confirmText,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = "Cancel",
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SelectedCount = ModList.SelectedItems.Count;

    // ── Drag & drop install ────────────────────────────────────

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = HasFiles(e) && !_viewModel.IsInstalling;
        Window_DragOver(sender, e);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && !_viewModel.IsInstalling ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only hide when the cursor left the window.
        var pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
            _viewModel.IsDragOver = false;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await _viewModel.InstallAsync(paths);
    }
}
